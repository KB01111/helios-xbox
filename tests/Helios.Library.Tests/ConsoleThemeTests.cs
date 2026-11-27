using Helios.Library;

namespace Helios.Library.Tests;

public sealed class ConsoleThemeTests
{
    [Fact]
    public void Presets_RoundTripThroughJson()
    {
        foreach (var preset in ConsoleTheme.Presets)
            Assert.Equal(preset, ConsoleTheme.FromJson(preset.ToJson()));
        Assert.Contains("\"layout\": \"spotlight\"", ConsoleTheme.Presets[1].ToJson());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("""{"name":"x","layout":"wheel","accent":"#FFFFFF","shape":"landscape","corners":"round","backdrop":"gameArt"}""")]
    [InlineData("""{"name":"x","layout":7,"accent":"#FFFFFF","shape":"landscape","corners":"round","backdrop":"gameArt"}""")]
    public void FromJson_RejectsMalformedThemes(string? json) => Assert.Null(ConsoleTheme.FromJson(json));

    [Fact]
    public void FromJson_NormalizesUnsafeValues()
    {
        var theme = ConsoleTheme.FromJson("""
            {"name":"  ","layout":"grid","accent":"red","shape":"portrait","corners":"soft",
             "backdrop":"picture","picture":"\\\\server\\share\\bg.png"}
            """);
        Assert.NotNull(theme);
        Assert.Equal("Custom", theme.Name);
        Assert.Equal(ConsoleTheme.Default.Accent, theme.Accent);
        Assert.Null(theme.Picture);
        Assert.Equal(BackdropStyle.GameArt, theme.Backdrop);

        var local = ConsoleTheme.FromJson("""
            {"name":"Mine","layout":"cinema","accent":"#a0b0c0","shape":"square","corners":"square",
             "backdrop":"picture","picture":"C:\\Pictures\\bg.jpg","showHints":false}
            """);
        Assert.Equal(@"C:\Pictures\bg.jpg", local!.Picture);
        Assert.Equal("#A0B0C0", local.Accent);
        Assert.False(local.ShowHints);
        Assert.Null(ConsoleTheme.FromJson(new string(' ', 20_000)));
    }

    [Fact]
    public void Colors_ParseAndPickReadableText()
    {
        Assert.True(ConsoleTheme.TryParseColor("#FFB86B", out var amber));
        Assert.Equal((255, 184, 107), amber);
        Assert.False(ConsoleTheme.TryParseColor("FFB86B", out _));
        Assert.False(ConsoleTheme.TryParseColor("#GGGGGG", out _));
        Assert.True(ConsoleTheme.PrefersDarkText(amber));
        Assert.False(ConsoleTheme.PrefersDarkText((30, 40, 120)));
        foreach (string swatch in ConsoleTheme.AccentSwatches)
            Assert.True(ConsoleTheme.TryParseColor(swatch, out _));
    }

    [Fact]
    public void TileMetrics_KeepShapeProportions()
    {
        Assert.Equal((300, 169), TileMetrics.Size(HomeLayout.Shelves, TileShape.Landscape));
        var (width, height) = TileMetrics.Size(HomeLayout.Grid, TileShape.Portrait);
        Assert.Equal(2.0 / 3, width / height, 2);
        var square = TileMetrics.Size(HomeLayout.Cinema, TileShape.Square);
        Assert.Equal(square.Width, square.Height);
        Assert.True(TileMetrics.CornerRadius(CornerStyle.Round) > TileMetrics.CornerRadius(CornerStyle.Square));
    }

    [Theory]
    [InlineData(@"C:\Steam\appcache\librarycache\42_library_hero.jpg", "42", @"C:\Steam\appcache\librarycache\42_library_600x900.jpg")]
    [InlineData(@"C:\Steam\appcache\librarycache\42\abc\library_hero.jpg", "42", null)]
    public void SteamCover_FindsLegacyPortraitArt(string artwork, string appId, string? expected)
    {
        var root = Directory.CreateTempSubdirectory("helios-cover-").FullName;
        try
        {
            string Rebase(string path) => Path.Combine(root, path[3..]);
            string art = Rebase(artwork);
            Directory.CreateDirectory(Path.GetDirectoryName(art)!);
            if (expected is not null)
                File.WriteAllText(Rebase(expected), "x");
            Assert.Equal(expected is null ? null : Rebase(expected), SteamLibrary.FindCover(art, appId));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SteamCover_SearchesTheAppFolder()
    {
        var root = Directory.CreateTempSubdirectory("helios-cover-").FullName;
        try
        {
            string app = Directory.CreateDirectory(Path.Combine(root, "librarycache", "42")).FullName;
            string hero = Path.Combine(Directory.CreateDirectory(Path.Combine(app, "h1")).FullName, "library_hero.jpg");
            string cover = Path.Combine(Directory.CreateDirectory(Path.Combine(app, "c2")).FullName, "library_600x900.jpg");
            File.WriteAllText(hero, "x");
            File.WriteAllText(cover, "x");
            Assert.Equal(cover, SteamLibrary.FindCover(hero, "42"));
            Assert.Null(SteamLibrary.FindCover(null, "42"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
