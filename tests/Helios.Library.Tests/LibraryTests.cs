using Helios.Library;

namespace Helios.Library.Tests;

public sealed class LibraryTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("helios-test-").FullName;

    [Fact]
    public void Refresh_PartialScanPreservesCachedGamesAndPreferences()
    {
        var store = new LibraryStore(Path.Combine(_root, "games.db"));
        var game = new GameEntry("steam:42", "Fourth Planet", GameSource.Steam, "42");
        store.ApplyScan(GameSource.Steam, [game], complete: true);
        store.SetFavorite(game.Id, true);
        store.RecordLaunch(game.Id, DateTimeOffset.Parse("2026-11-27T12:30:00Z"));

        store.ApplyScan(GameSource.Steam, [], complete: false);
        var cached = Assert.Single(store.ReadGames());
        Assert.True(cached.IsAvailable);
        Assert.True(cached.IsFavorite);
        Assert.NotNull(cached.LastLaunch);

        store.ApplyScan(GameSource.Steam, [], complete: true);
        Assert.False(Assert.Single(store.ReadGames()).IsAvailable);
        store.ApplyScan(GameSource.Steam, [game with { Title = "New title" }], complete: true);
        var restored = Assert.Single(new LibraryStore(Path.Combine(_root, "games.db")).ReadGames());
        Assert.True(restored.IsAvailable);
        Assert.True(restored.IsFavorite);
        Assert.Equal("New title", restored.Title);
    }

    [Fact]
    public void Scan_ReadsSecondaryLibraryAndIgnoresToolsAndUninstalledEntries()
    {
        var primary = Directory.CreateDirectory(Path.Combine(_root, "Steam", "steamapps"));
        var secondary = Directory.CreateDirectory(Path.Combine(_root, "Second drive", "steamapps"));
        string escaped = secondary.Parent!.FullName.Replace("\\", "\\\\");
        File.WriteAllText(Path.Combine(primary.FullName, "libraryfolders.vdf"),
            $$""" "libraryfolders" { "1" { "path" "{{escaped}}" } } """);
        WriteManifest(secondary.FullName, 42, """ Orbital \"Station\" """.Trim(), 6);
        WriteManifest(secondary.FullName, 43, "Not installed", 2);
        WriteManifest(primary.FullName, 228980, "Steamworks Common Redistributables", 4);

        var scan = SteamLibrary.Scan(primary.Parent!.FullName);

        Assert.True(scan.Complete);
        var game = Assert.Single(scan.Games);
        Assert.Equal("steam:42", game.Id);
        Assert.Equal("Orbital \"Station\"", game.Title);
    }

    [Fact]
    public void Scan_BadManifestRetainsValidGamesAndReportsIncompleteScan()
    {
        var steamApps = Directory.CreateDirectory(Path.Combine(_root, "steamapps"));
        File.WriteAllText(Path.Combine(steamApps.FullName, "appmanifest_41.acf"), "\"AppState\" {");
        WriteManifest(steamApps.FullName, 42, "Orbital Station", 4);

        var scan = SteamLibrary.Scan(_root);

        Assert.False(scan.Complete);
        Assert.Equal("42", Assert.Single(scan.Games).Target);
    }

    [Theory]
    [InlineData(GameSource.Steam, "123", true)]
    [InlineData(GameSource.Steam, "123/../../other", false)]
    [InlineData(GameSource.Windows, "Pkg.Name_publisher!Game", true)]
    [InlineData(GameSource.Windows, "ms-settings:privacy", false)]
    [InlineData(GameSource.Manual, @"C:\Games\Title.exe", true)]
    [InlineData(GameSource.Manual, @"C:\Games\Title.lnk", true)]
    [InlineData(GameSource.Manual, @"C:\Games\Title.cmd", false)]
    [InlineData(GameSource.Manual, @"Games\Title.exe", false)]
    public void TargetValidation_RestrictsTargetType(GameSource source, string value, bool valid) =>
        Assert.Equal(valid, GameEntry.IsValidTarget(source, value));

    private static void WriteManifest(string folder, int id, string name, int flags) =>
        File.WriteAllText(Path.Combine(folder, $"appmanifest_{id}.acf"),
            $$""" "AppState" { "appid" "{{id}}" "name" "{{name}}" "StateFlags" "{{flags}}" } """);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
