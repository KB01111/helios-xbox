using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Helios.Library;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Core;
using Windows.Foundation;
using Windows.Management.Deployment;

namespace Helios_Launcher.Windows;

internal static class WindowsGameLibrary
{
    internal static async Task<GameScan> ScanAsync(string artworkDirectory, IReadOnlySet<string>? pinned = null, bool allApps = false)
    {
        var titles = new Dictionary<string, GameEntry>(StringComparer.OrdinalIgnoreCase);
        bool complete = true;
        var manager = new PackageManager();
        if (!allApps)
            Directory.CreateDirectory(artworkDirectory);

        try
        {
            foreach (Package package in manager.FindPackagesForUser(string.Empty))
            {
                try
                {
                    if (package.IsFramework || package.IsResourcePackage || package.IsBundle
                        || package.SignatureKind != PackageSignatureKind.Store)
                        continue;
                    bool isGame = !allApps && IsGame(package);
                    string family = package.Id.FamilyName + "!";
                    if (!allApps && !isGame && (pinned is null || !pinned.Any(id => id.StartsWith(family, StringComparison.OrdinalIgnoreCase))))
                        continue;

                    foreach (AppListEntry entry in await package.GetAppListEntriesAsync())
                    {
                        if (!GameEntry.IsValidTarget(GameSource.Windows, entry.AppUserModelId)
                            || (!allApps && !isGame && pinned?.Contains(entry.AppUserModelId) != true))
                            continue;

                        string? art = allApps ? null : await CacheArtworkAsync(artworkDirectory, package, entry);
                        string name = entry.DisplayInfo.DisplayName;
                        if (!string.IsNullOrWhiteSpace(name))
                            titles.TryAdd(entry.AppUserModelId, new GameEntry(
                                $"windows:{entry.AppUserModelId}", name, GameSource.Windows, entry.AppUserModelId, art));
                    }
                }
                catch (Exception error) when (error is COMException or IOException or UnauthorizedAccessException)
                {
                    complete = false;
                }
            }
        }
        catch (Exception error) when (error is COMException or IOException or UnauthorizedAccessException)
        {
            complete = false;
        }

        return new GameScan(titles.Values.OrderBy(game => game.Title, StringComparer.CurrentCultureIgnoreCase).ToList(), complete);
    }

    private static bool IsGame(Package package)
    {
        string path = package.InstalledLocation.Path;
        return File.Exists(Path.Combine(path, "MicrosoftGame.config"))
            || File.Exists(Path.Combine(path, "Content", "MicrosoftGame.config"));
    }

    private static async Task<string?> CacheArtworkAsync(string directory, Package package, AppListEntry entry)
    {
        string stamp = $"{package.Id.FullName}:{entry.AppUserModelId}";
        string name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(stamp))) + ".png";
        string path = Path.Combine(directory, name);
        if (File.Exists(path))
            return path;

        try
        {
            using var source = await entry.DisplayInfo.GetLogo(new Size(512, 512)).OpenReadAsync();
            if (source.Size == 0 || source.Size > 8 * 1024 * 1024)
                return null;
            string temporary = path + ".tmp";
            try
            {
                await using (var output = File.Create(temporary))
                    await source.AsStreamForRead().CopyToAsync(output);
                File.Move(temporary, path, overwrite: true);
                return path;
            }
            finally
            {
                File.Delete(temporary);
            }
        }
        catch (Exception error) when (error is COMException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
