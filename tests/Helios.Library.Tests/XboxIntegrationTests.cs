using Helios.Library;

namespace Helios.Library.Tests;

public sealed class XboxIntegrationTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("helios-xbox-").FullName;

    [Theory]
    [InlineData("Protocol", "windows.gaming:///home", LaunchTarget.Home, true)]
    [InlineData("Protocol", "windows.gaming:///library", LaunchTarget.Library, true)]
    [InlineData("Protocol", "WINDOWS.GAMING:///Library", LaunchTarget.Library, true)]
    [InlineData("Protocol", "helios-launcher://library", LaunchTarget.Library, false)]
    [InlineData("Protocol", "helios-launcher://home", LaunchTarget.Home, false)]
    [InlineData("Protocol", "https://example.com/library", LaunchTarget.Home, false)]
    [InlineData("Launch", null, LaunchTarget.Home, false)]
    [InlineData("Protocol", "not a uri", LaunchTarget.Home, false)]
    public void LaunchRequest_MapsGameBarButtons(string kind, string? uri, LaunchTarget target, bool gaming)
    {
        var request = LaunchRequest.Parse(kind, uri);
        Assert.Equal(target, request.Target);
        Assert.Equal(gaming, request.FromGamingShell);
    }

    [Fact]
    public void LaunchRequest_DescribesForTheLog()
    {
        Assert.Equal("Protocol:gaming/library", LaunchRequest.Parse("Protocol", "windows.gaming:///library").Describe());
        Assert.Equal("StartupTask", LaunchRequest.Parse("StartupTask", null).Describe());
        Assert.True(LaunchRequest.Parse("StartupTask", null).FromStartupTask);
    }

    [Fact]
    public void HomeState_ObservedActivationCountsOnlyWhenRegistryIsSilent()
    {
        const string helios = "KB01111.HeliosLauncher_abc!App";
        Assert.True(GamingHomeState.From(null, null, helios, observedAsHome: true).IsHelios);
        Assert.False(GamingHomeState.From(GamingHomeState.XboxAppId, null, helios, observedAsHome: true).IsHelios);
        Assert.False(GamingHomeState.From(null, null, helios).IsHelios);
    }

    [Fact]
    public void PeekSettings_ReadsWithoutCreatingTheDatabase()
    {
        string path = Path.Combine(_root, "library.db");
        Assert.Empty(LibraryStore.PeekSettings(path, "fullScreen"));
        Assert.False(File.Exists(path));

        var store = new LibraryStore(path);
        store.SetSetting("fullScreen", "false");
        store.SetSetting("theme", "{}");
        var values = LibraryStore.PeekSettings(path, "fullScreen", "theme", "missing");
        Assert.Equal("false", values["fullScreen"]);
        Assert.Equal("{}", values["theme"]);
        Assert.False(values.ContainsKey("missing"));

        File.WriteAllBytes(Path.Combine(_root, "bad.db"), [1, 2, 3]);
        Assert.Empty(LibraryStore.PeekSettings(Path.Combine(_root, "bad.db"), "theme"));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
