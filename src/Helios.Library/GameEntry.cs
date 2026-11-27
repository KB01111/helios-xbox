using System.Globalization;

namespace Helios.Library;

public enum GameSource
{
    Steam,
    Windows,
    Manual,
}

public sealed record GameEntry(
    string Id,
    string Title,
    GameSource Source,
    string Target,
    string? Artwork = null,
    bool IsAvailable = true,
    bool IsFavorite = false,
    DateTimeOffset? LastLaunch = null,
    bool IsPinned = false)
{
    public static bool IsValidTarget(GameSource source, string value) => source switch
    {
        GameSource.Steam => uint.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0,
        GameSource.Windows => IsAppIdentity(value),
        GameSource.Manual => Path.IsPathFullyQualified(value)
            && !value.StartsWith(@"\\", StringComparison.Ordinal)
            && (Path.GetExtension(value).Equals(".exe", StringComparison.OrdinalIgnoreCase)
                || Path.GetExtension(value).Equals(".lnk", StringComparison.OrdinalIgnoreCase)),
        _ => false,
    };

    private static bool IsAppIdentity(string value)
    {
        int separator = value.IndexOf('!');
        return separator > 0 && separator < value.Length - 1
            && value[..separator].Contains('_')
            && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-' or '!')
            && value.LastIndexOf('!') == separator;
    }
}

public sealed record GameScan(IReadOnlyList<GameEntry> Games, bool Complete);
