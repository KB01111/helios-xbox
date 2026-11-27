using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Helios.Library;

/// <summary>How the Home screen arranges the focused game and the shelves.</summary>
public enum HomeLayout
{
    /// <summary>Hero over the art, a "Jump back in" shelf and a Favorites shelf.</summary>
    Shelves,
    /// <summary>A tall hero for the focused game with one compact shelf along the bottom.</summary>
    Spotlight,
    /// <summary>Full-strength art, minimal text and a slim carousel: a poster-like view for TVs.</summary>
    Cinema,
    /// <summary>A short header and every game in a wrapping grid.</summary>
    Grid,
}

public enum TileShape { Landscape, Portrait, Square }

public enum CornerStyle { Square, Soft, Round }

public enum BackdropStyle { GameArt, Picture, Solid }

/// <summary>A console-mode appearance. Stored as JSON in the settings table and shareable as a .helios-theme.json file.</summary>
public sealed record ConsoleTheme(
    string Name,
    HomeLayout Layout,
    string Accent,
    TileShape Shape,
    CornerStyle Corners,
    BackdropStyle Backdrop,
    string? Picture = null,
    bool ShowHints = true,
    bool ShowClock = true)
{
    public const string FileExtension = ".helios-theme.json";
    private const int MaxJsonLength = 16 * 1024;

    public static ConsoleTheme Default { get; } = new("Ember", HomeLayout.Shelves, "#FFB86B", TileShape.Landscape, CornerStyle.Round, BackdropStyle.GameArt);

    public static IReadOnlyList<ConsoleTheme> Presets { get; } =
    [
        Default,
        new("Tide", HomeLayout.Spotlight, "#6FD3C4", TileShape.Landscape, CornerStyle.Soft, BackdropStyle.GameArt),
        new("Marquee", HomeLayout.Cinema, "#F4E4C1", TileShape.Portrait, CornerStyle.Soft, BackdropStyle.GameArt),
        new("Arcade", HomeLayout.Grid, "#FF7A9C", TileShape.Portrait, CornerStyle.Round, BackdropStyle.GameArt),
        new("Slate", HomeLayout.Shelves, "#9DB0FF", TileShape.Square, CornerStyle.Square, BackdropStyle.Solid),
    ];

    public static IReadOnlyList<string> AccentSwatches { get; } =
        ["#FFB86B", "#FF7A9C", "#C79BFF", "#9DB0FF", "#6FD3C4", "#8BD873", "#F4E4C1", "#F5D25B"];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    /// <summary>Parses and validates a theme. Returns null for anything malformed or unsafe.</summary>
    public static ConsoleTheme? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > MaxJsonLength)
            return null;
        try
        {
            var theme = JsonSerializer.Deserialize<ConsoleTheme>(json, JsonOptions);
            return theme?.Normalize();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Clamps values from files and older versions to something the UI can always render.</summary>
    public ConsoleTheme? Normalize()
    {
        if (!Enum.IsDefined(Layout) || !Enum.IsDefined(Shape) || !Enum.IsDefined(Corners) || !Enum.IsDefined(Backdrop))
            return null;
        string name = string.IsNullOrWhiteSpace(Name) ? "Custom" : Name.Trim();
        string? picture = IsLocalPicture(Picture) ? Picture : null;
        return this with
        {
            Name = name.Length > 40 ? name[..40] : name,
            Accent = TryParseColor(Accent, out _) ? Accent.ToUpperInvariant() : Default.Accent,
            Picture = picture,
            Backdrop = Backdrop == BackdropStyle.Picture && picture is null ? BackdropStyle.GameArt : Backdrop,
        };
    }

    public static bool TryParseColor(string? value, out (byte R, byte G, byte B) color)
    {
        color = default;
        if (value is not { Length: 7 } || value[0] != '#'
            || !uint.TryParse(value.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgb))
            return false;
        color = ((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        return true;
    }

    /// <summary>True when text on the accent should be dark (WCAG relative luminance above the midpoint).</summary>
    public static bool PrefersDarkText((byte R, byte G, byte B) color)
    {
        static double Channel(byte value)
        {
            double c = value / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
        double luminance = 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
        return luminance > 0.179;
    }

    private static bool IsLocalPicture(string? path) =>
        path is not null && Path.IsPathFullyQualified(path) && !path.StartsWith(@"\\", StringComparison.Ordinal)
        && Path.GetExtension(path).ToLowerInvariant() is ".jpg" or ".jpeg" or ".png" or ".webp" or ".bmp";
}

/// <summary>Tile sizes in logical pixels for each layout and shape.</summary>
public static class TileMetrics
{
    public static (double Width, double Height) Size(HomeLayout layout, TileShape shape)
    {
        double height = layout switch
        {
            HomeLayout.Spotlight or HomeLayout.Cinema => shape == TileShape.Portrait ? 186 : 128,
            HomeLayout.Grid => shape == TileShape.Portrait ? 240 : 160,
            _ => shape == TileShape.Portrait ? 228 : 169,
        };
        double width = shape switch
        {
            TileShape.Portrait => Math.Round(height * 2 / 3),
            TileShape.Square => height,
            _ => Math.Round(height * 16 / 9),
        };
        return (width, height);
    }

    public static double CornerRadius(CornerStyle corners) => corners switch
    {
        CornerStyle.Square => 2,
        CornerStyle.Soft => 8,
        _ => 14,
    };
}
