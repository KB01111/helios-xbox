using System.Numerics;
using Helios.Library;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Helios_Launcher;

/// <summary>Size, shape and corners for a set of tiles, derived from the console theme.</summary>
internal sealed record TileStyle(double Width, double Height, double Corner, TileShape Shape)
{
    internal static TileStyle For(ConsoleTheme theme, HomeLayout layout)
    {
        var (width, height) = TileMetrics.Size(layout, theme.Shape);
        return new TileStyle(width, height, TileMetrics.CornerRadius(theme.Corners), theme.Shape);
    }
}

internal sealed class Tile(GameEntry game, TileStyle style)
{
    public GameEntry Game { get; } = game;
    public string Title => Game.Title;
    public string Subtitle => Game.IsAvailable ? SourceLabel(Game.Source) : $"{SourceLabel(Game.Source)} · Unavailable";
    public string AccessibleName => Game.IsFavorite ? $"{Title}, favorite, {Subtitle}" : $"{Title}, {Subtitle}";
    public string Initials => string.Concat(Title.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(part => part[0])).ToUpperInvariant();

    public double TileWidth => style.Width;
    public double TileHeight => style.Height;
    public CornerRadius Corner => new(style.Corner);
    public Vector3 Center => new((float)style.Width / 2, (float)style.Height / 2, 0);
    public double TitleSize => style.Width < 200 ? 14 : 16;

    // Portrait themes use Steam's 600x900 cover when the client cached one.
    public ImageSource? Image => LoadArtwork(
        style.Shape == TileShape.Portrait && Game.Source == GameSource.Steam ? CoverFor(Game) ?? Game.Artwork : Game.Artwork,
        (int)Math.Ceiling(style.Width * 1.1));

    // Store logos are square, transparent icons: show them whole above the title instead of cropping them.
    public Stretch ArtStretch => Game.Source == GameSource.Windows ? Stretch.Uniform : Stretch.UniformToFill;

    public Thickness ArtMargin
    {
        get
        {
            if (Game.Source != GameSource.Windows)
                return new Thickness(0);
            double box = Math.Min(style.Width * 0.45, style.Height * 0.5);
            double side = (style.Width - box) / 2;
            double top = Math.Max(8, (style.Height - 44 - box) / 2);
            return new Thickness(side, top, side, Math.Max(0, style.Height - top - box));
        }
    }

    // The initials are only a fallback for games without art; transparent logos would otherwise show them through.
    public Visibility InitialsVisibility => Image is null ? Visibility.Visible : Visibility.Collapsed;
    public Visibility FavoriteBadge => Game.IsFavorite ? Visibility.Visible : Visibility.Collapsed;
    public double ArtOpacity => Game.IsAvailable ? 1 : .4;

    internal static string SourceLabel(GameSource source) => source switch
    {
        GameSource.Steam => "Steam",
        GameSource.Windows => "Xbox & Store",
        _ => "Manual",
    };

    internal static string LastPlayed(DateTimeOffset? date)
    {
        if (date is not { } value)
            return "Not played yet";
        var age = DateTimeOffset.UtcNow - value;
        return age.TotalMinutes < 2 ? "Played just now"
            : age.TotalHours < 1 ? $"Played {(int)age.TotalMinutes} minutes ago"
            : age.TotalHours < 2 ? "Played an hour ago"
            : age.TotalDays < 1 ? $"Played {(int)age.TotalHours} hours ago"
            : age.TotalDays < 2 ? "Played yesterday"
            : age.TotalDays < 30 ? $"Played {(int)age.TotalDays} days ago"
            : $"Played {value.ToLocalTime():d}";
    }

    private static readonly Dictionary<string, string?> Covers = [];

    private static string? CoverFor(GameEntry game)
    {
        if (game.Artwork is not { } artwork)
            return null;
        if (!Covers.TryGetValue(artwork, out string? cover))
            Covers[artwork] = cover = SteamLibrary.FindCover(artwork, game.Target);
        return cover;
    }

    // Decoded bitmaps are shared across views so focus changes and library refreshes never re-decode artwork.
    private static readonly Dictionary<(string Path, int Width), BitmapImage> Cache = [];
    private static readonly Queue<(string Path, int Width)> Order = new();
    private const int CacheLimit = 320;

    internal static BitmapImage? LoadArtwork(string? path, int width)
    {
        if (path is null)
            return null;
        if (Cache.TryGetValue((path, width), out var cached))
            return cached;
        if (!Uri.TryCreate(path, UriKind.Absolute, out Uri? uri) || !uri.IsFile || uri.IsUnc || !File.Exists(path))
            return null;
        var image = new BitmapImage { DecodePixelWidth = width, DecodePixelType = DecodePixelType.Logical, UriSource = uri };
        Cache[(path, width)] = image;
        Order.Enqueue((path, width));
        if (Order.Count > CacheLimit)
            Cache.Remove(Order.Dequeue());
        return image;
    }

    /// <summary>Drops cached bitmaps that are not on screen, so Helios stays small while a game runs.</summary>
    internal static void TrimCache()
    {
        Cache.Clear();
        Order.Clear();
    }
}
