using Helios.Library;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Helios_Launcher;

internal sealed class Tile(GameEntry game)
{
    internal const int TileDecodeWidth = 480;

    public GameEntry Game { get; } = game;
    public string Title => Game.Title;
    public string Subtitle => Game.IsAvailable ? SourceLabel(Game.Source) : $"{SourceLabel(Game.Source)} · Unavailable";
    public string AccessibleName => Game.IsFavorite ? $"{Title}, favorite, {Subtitle}" : $"{Title}, {Subtitle}";
    public string Initials => string.Concat(Title.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(part => part[0])).ToUpperInvariant();
    public ImageSource? Image => LoadArtwork(Game.Artwork, TileDecodeWidth);

    // Store logos are square, transparent icons: show them whole on a tinted tile instead of cropping them.
    public Stretch ArtStretch => Game.Source == GameSource.Windows ? Stretch.Uniform : Stretch.UniformToFill;
    public Thickness ArtMargin => Game.Source == GameSource.Windows ? new Thickness(90, 20, 90, 58) : new Thickness(0);
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
}
