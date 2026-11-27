using Helios.Library;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Helios_Launcher;

internal sealed class Tile(GameEntry game)
{
    public GameEntry Game { get; } = game;
    public string Title => Game.Title;
    public string Subtitle => Game.IsAvailable ? SourceLabel(Game.Source) : $"{SourceLabel(Game.Source)} · Unavailable";
    public string AccessibleName => $"{Title}, {Subtitle}";
    public string Initials => string.Concat(Title.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(part => part[0])).ToUpperInvariant();
    public ImageSource? Image => LoadArtwork(Game.Artwork, 460);

    internal static string SourceLabel(GameSource source) => source switch
    {
        GameSource.Steam => "Steam",
        GameSource.Windows => "Xbox & Store",
        _ => "Manual",
    };

    internal static BitmapImage? LoadArtwork(string? path, int width) =>
        path is not null && Uri.TryCreate(path, UriKind.Absolute, out Uri? uri) && uri.IsFile && !uri.IsUnc && File.Exists(path)
            ? new BitmapImage { DecodePixelWidth = width, UriSource = uri }
            : null;
}
