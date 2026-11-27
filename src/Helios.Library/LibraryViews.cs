namespace Helios.Library;

public enum LibraryFilter
{
    All,
    Steam,
    Windows,
    Manual,
    Favorites,
}

/// <summary>Pure ordering and filtering rules for the launcher views.</summary>
public static class LibraryViews
{
    public static IReadOnlyList<LibraryFilter> Filters { get; } = Enum.GetValues<LibraryFilter>();

    public static string Label(LibraryFilter filter) => filter switch
    {
        LibraryFilter.Steam => "Steam",
        LibraryFilter.Windows => "Xbox & Store",
        LibraryFilter.Manual => "Manual",
        LibraryFilter.Favorites => "Favorites",
        _ => "All games",
    };

    /// <summary>
    /// The Home shelf: recently launched games first (newest first), then favorites, then everything else by title.
    /// Unavailable games sink to the end so the first tile is always playable when possible.
    /// </summary>
    public static IReadOnlyList<GameEntry> HomeShelf(IEnumerable<GameEntry> games, int limit = 24) =>
        games
            .OrderBy(game => game.IsAvailable ? 0 : 1)
            .ThenBy(game => game.LastLaunch is null ? 1 : 0)
            .ThenByDescending(game => game.LastLaunch)
            .ThenBy(game => game.IsFavorite ? 0 : 1)
            .ThenBy(game => game.Title, StringComparer.CurrentCultureIgnoreCase)
            .Take(limit)
            .ToList();

    public static IReadOnlyList<GameEntry> Favorites(IEnumerable<GameEntry> games) =>
        games.Where(game => game.IsFavorite)
            .OrderBy(game => game.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    public static IReadOnlyList<GameEntry> Filter(IEnumerable<GameEntry> games, string? query, LibraryFilter filter)
    {
        string search = query?.Trim() ?? string.Empty;
        return games
            .Where(game => search.Length == 0 || game.Title.Contains(search, StringComparison.CurrentCultureIgnoreCase))
            .Where(game => filter switch
            {
                LibraryFilter.Steam => game.Source == GameSource.Steam,
                LibraryFilter.Windows => game.Source == GameSource.Windows,
                LibraryFilter.Manual => game.Source == GameSource.Manual,
                LibraryFilter.Favorites => game.IsFavorite,
                _ => true,
            })
            .ToList();
    }

    public static LibraryFilter Cycle(LibraryFilter current, int step)
    {
        int count = Filters.Count;
        return Filters[(((int)current + step) % count + count) % count];
    }
}
