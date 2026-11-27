using Helios.Library;

namespace Helios.Library.Tests;

public sealed class LibraryViewsTests
{
    private static readonly GameEntry Alpha = new("steam:1", "Alpha", GameSource.Steam, "1");
    private static readonly GameEntry Beta = new("manual:b", "Beta", GameSource.Manual, @"C:\Games\Beta.exe", IsFavorite: true);
    private static readonly GameEntry Gamma = new("steam:3", "Gamma", GameSource.Steam, "3",
        LastLaunch: DateTimeOffset.Parse("2026-11-20T10:00:00Z"));
    private static readonly GameEntry Delta = new("windows:d", "Delta", GameSource.Windows, "Fam_x!App",
        LastLaunch: DateTimeOffset.Parse("2026-11-26T10:00:00Z"));
    private static readonly GameEntry Gone = new("steam:9", "Aardvark", GameSource.Steam, "9", IsAvailable: false,
        LastLaunch: DateTimeOffset.Parse("2026-11-27T10:00:00Z"));

    [Fact]
    public void HomeShelf_OrdersRecentThenFavoritesThenTitleWithUnavailableLast()
    {
        var shelf = LibraryViews.HomeShelf([Alpha, Beta, Gamma, Delta, Gone]);
        Assert.Equal(["Delta", "Gamma", "Beta", "Alpha", "Aardvark"], shelf.Select(game => game.Title));
        Assert.Equal(2, LibraryViews.HomeShelf([Alpha, Beta, Gamma], limit: 2).Count);
    }

    [Fact]
    public void Filter_CombinesSearchAndSource()
    {
        GameEntry[] games = [Alpha, Beta, Gamma, Delta];
        Assert.Equal(["Alpha", "Gamma"], LibraryViews.Filter(games, null, LibraryFilter.Steam).Select(game => game.Title));
        Assert.Equal(["Gamma"], LibraryViews.Filter(games, "  gam ", LibraryFilter.All).Select(game => game.Title));
        Assert.Equal(["Beta"], LibraryViews.Filter(games, "", LibraryFilter.Favorites).Select(game => game.Title));
        Assert.Empty(LibraryViews.Filter(games, "zzz", LibraryFilter.All));
    }

    [Fact]
    public void Cycle_WrapsInBothDirections()
    {
        Assert.Equal(LibraryFilter.Favorites, LibraryViews.Cycle(LibraryFilter.All, -1));
        Assert.Equal(LibraryFilter.All, LibraryViews.Cycle(LibraryFilter.Favorites, 1));
        Assert.Equal(LibraryFilter.Steam, LibraryViews.Cycle(LibraryFilter.All, 1));
    }

    [Theory]
    [InlineData("KB01111.HeliosLauncher_5kwfp535vx8rc!App", true, "Helios")]
    [InlineData("kb01111.helioslauncher_5kwfp535vx8rc!app", true, "Helios")]
    [InlineData(GamingHomeState.XboxAppId, false, "Xbox app")]
    [InlineData(null, false, "Xbox app")]
    [InlineData("ArtemShpynov.AnyFSE_by4wjhxmygwr8!App", false, "ArtemShpynov.AnyFSE")]
    public void GamingHome_RecognizesHelios(string? configured, bool isHelios, string name)
    {
        var state = GamingHomeState.From(configured, true, "KB01111.HeliosLauncher_5kwfp535vx8rc!App");
        Assert.Equal(isHelios, state.IsHelios);
        Assert.Equal(name, state.HomeDisplayName);
        Assert.False(GamingHomeState.From(configured, null, null).IsHelios);
    }
}
