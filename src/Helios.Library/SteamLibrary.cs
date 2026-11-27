using System.Globalization;
using System.Text.RegularExpressions;

namespace Helios.Library;

public static partial class SteamLibrary
{
    public static GameScan Scan(string? root)
    {
        if (string.IsNullOrWhiteSpace(root))
            return new GameScan([], false);

        string steamApps = Path.Combine(root, "steamapps");
        var libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { root };
        var games = new Dictionary<string, GameEntry>(StringComparer.Ordinal);
        bool complete = true;
        try
        {
            var folders = ReadVdf(Path.Combine(steamApps, "libraryfolders.vdf"));
            if (folders.GetValueOrDefault("libraryfolders") is not Dictionary<string, object> configured)
                throw new FormatException("Missing Steam library folders.");

            foreach (var (key, value) in configured)
            {
                if (!uint.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out _))
                    continue;
                string? path = value is Dictionary<string, object> details
                    ? details.GetValueOrDefault("path") as string
                    : value as string;
                if (!string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path)
                    && !path.StartsWith(@"\\", StringComparison.Ordinal))
                    libraries.Add(Path.GetFullPath(path));
            }
        }
        catch (FileNotFoundException)
        {
            // Single-drive Steam installations can have no libraryfolders file.
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or FormatException or ArgumentException)
        {
            complete = false;
        }

        foreach (string library in libraries)
        {
            try
            {
                foreach (string file in Directory.EnumerateFiles(Path.Combine(library, "steamapps"), "appmanifest_*.acf"))
                {
                    try
                    {
                        if (ReadGame(file, root) is { } game)
                            games.TryAdd(game.Id, game);
                    }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException or FormatException)
                    {
                        complete = false;
                    }
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                complete = false;
            }
        }

        return new GameScan(games.Values.ToArray(), complete);
    }

    private static GameEntry? ReadGame(string path, string steamRoot)
    {
        var manifest = ReadVdf(path);
        if (manifest.GetValueOrDefault("AppState") is not Dictionary<string, object> fields
            || fields.GetValueOrDefault("appid") is not string appId
            || !GameEntry.IsValidTarget(GameSource.Steam, appId)
            || fields.GetValueOrDefault("name") is not string title
            || string.IsNullOrWhiteSpace(title)
            || fields.GetValueOrDefault("StateFlags") is not string state
            || !uint.TryParse(state, NumberStyles.None, CultureInfo.InvariantCulture, out uint flags))
            throw new FormatException("Invalid Steam application manifest.");

        if ((flags & 4) == 0 || appId == "228980")
            return null;

        return new GameEntry($"steam:{appId}", title.Trim(), GameSource.Steam, appId, FindArtwork(steamRoot, appId));
    }

    /// <summary>
    /// Finds the 600×900 portrait cover next to a game's stored Steam artwork, for portrait tile themes.
    /// Handles both the legacy flat cache ({appid}_name.jpg) and the per-app folders of newer clients.
    /// </summary>
    public static string? FindCover(string? artwork, string appId)
    {
        if (artwork is null || !Path.IsPathFullyQualified(artwork))
            return null;
        try
        {
            string? directory = Path.GetDirectoryName(artwork);
            if (directory is null)
                return null;
            if (Path.GetFileName(artwork).StartsWith(appId + "_", StringComparison.Ordinal))
            {
                string legacy = Path.Combine(directory, $"{appId}_library_600x900.jpg");
                return File.Exists(legacy) ? legacy : null;
            }
            for (var folder = new DirectoryInfo(directory); folder is not null; folder = folder.Parent)
            {
                if (folder.Name != appId)
                    continue;
                return Directory.EnumerateFiles(folder.FullName, "library_600x900.jpg", SearchOption.AllDirectories).FirstOrDefault();
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Covers are optional.
        }
        return null;
    }

    private static string? FindArtwork(string root, string appId)
    {
        string cache = Path.Combine(root, "appcache", "librarycache");
        string[] fileNames = ["library_hero.jpg", "header.jpg", "library_600x900.jpg", "logo.png"];
        try
        {
            string appFolder = Path.Combine(cache, appId);
            foreach (string name in fileNames)
            {
                string legacy = Path.Combine(cache, $"{appId}_{name}");
                if (File.Exists(legacy))
                    return legacy;
                if (Directory.Exists(appFolder))
                {
                    string? asset = Directory.EnumerateFiles(appFolder, name, SearchOption.AllDirectories).FirstOrDefault();
                    if (asset is not null)
                        return asset;
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Artwork never blocks import.
        }

        return null;
    }

    private static Dictionary<string, object> ReadVdf(string path)
    {
        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length > 4 * 1024 * 1024)
            throw new FormatException("Steam manifest is too large.");
        using var reader = new StreamReader(stream);
        var text = reader.ReadToEnd();
        int offset = 0;
        return ReadObject(text, ref offset, 0);
    }

    private static Dictionary<string, object> ReadObject(string text, ref int offset, int depth)
    {
        if (depth > 24)
            throw new FormatException("Steam manifest nesting is too deep.");

        var fields = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        while (true)
        {
            string? key = ReadToken(text, ref offset);
            if ((key is null && depth == 0) || (key == "}" && depth != 0))
                return fields;
            if (key is null or "{" or "}")
                throw new FormatException("Expected a Steam manifest key.");

            string? value = ReadToken(text, ref offset);
            fields[key[1..]] = value switch
            {
                "{" => ReadObject(text, ref offset, depth + 1),
                null or "}" => throw new FormatException("Expected a Steam manifest value."),
                _ => value[1..],
            };
        }
    }

    private static string? ReadToken(string text, ref int offset)
    {
        offset += Trivia().Match(text, offset).Length;
        if (offset == text.Length)
            return null;
        Match match = Token().Match(text, offset);
        if (!match.Success)
            throw new FormatException("Invalid Steam manifest token.");
        offset += match.Length;
        return match.Value[0] switch
        {
            '"' => "v" + match.Value[1..^1].Replace("\\\"", "\"").Replace("\\\\", "\\"),
            '{' or '}' => match.Value,
            _ => "v" + match.Value,
        };
    }

    [GeneratedRegex("""\G(?:\s+|//[^\r\n]*)*""")]
    private static partial Regex Trivia();

    [GeneratedRegex("""\G(?:"(?:\\.|[^"\\])*"|[{}]|[^{}\s"]+)""")]
    private static partial Regex Token();
}
