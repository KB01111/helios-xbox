using System.Diagnostics;
using Helios.Library;
using Windows.Management.Deployment;

namespace Helios_Launcher.Windows;

internal static class GameLaunch
{
    internal static async Task StartAsync(GameEntry game)
    {
        if (!game.IsAvailable || !GameEntry.IsValidTarget(game.Source, game.Target))
            throw new InvalidOperationException("This game is unavailable. Refresh the library or add it again.");

        switch (game.Source)
        {
            case GameSource.Steam:
                Open($"steam://rungameid/{game.Target}");
                break;
            case GameSource.Windows:
                await ActivateAppAsync(game.Target);
                break;
            case GameSource.Manual:
                if (!File.Exists(game.Target))
                    throw new FileNotFoundException("The game moved or its drive is disconnected. Add it again from its new location.");
                Open(game.Target, Path.GetDirectoryName(game.Target));
                break;
        }
    }

    internal static void Open(string target, string? workingDirectory = null)
    {
        var start = new ProcessStartInfo(target) { UseShellExecute = true };
        if (workingDirectory is not null)
            start.WorkingDirectory = workingDirectory;
        using var process = Process.Start(start);
    }

    private static async Task ActivateAppAsync(string appId)
    {
        string family = appId[..appId.IndexOf('!')];
        var manager = new PackageManager();
        foreach (var package in manager.FindPackagesForUser(string.Empty, family))
        {
            if (package.IsResourcePackage || package.IsBundle)
                continue;
            foreach (var entry in await package.GetAppListEntriesAsync())
            {
                if (!string.Equals(entry.AppUserModelId, appId, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!await entry.LaunchAsync())
                    throw new InvalidOperationException("Windows did not accept the app launch.");
                return;
            }
        }

        throw new InvalidOperationException("Windows no longer lists this game. Refresh the library or reinstall it.");
    }
}
