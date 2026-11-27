using Helios.Library;

namespace Helios.Library.Tests;

public sealed class HardeningTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("helios-hardening-").FullName;

    [Fact]
    public void OpenOrRecover_HealthyLibraryIsKept()
    {
        string path = Path.Combine(_root, "library.db");
        var first = new LibraryStore(path);
        first.ApplyScan(GameSource.Steam, [new GameEntry("steam:42", "Orbital Station", GameSource.Steam, "42")], complete: true);

        var (store, quarantined) = LibraryStore.OpenOrRecover(path);

        Assert.Null(quarantined);
        Assert.Single(store.ReadGames());
    }

    [Fact]
    public void OpenOrRecover_GarbageFileIsMovedAsideAndReplaced()
    {
        string path = Path.Combine(_root, "library.db");
        File.WriteAllBytes(path, Enumerable.Repeat((byte)0x5A, 8192).ToArray());

        var (store, quarantined) = LibraryStore.OpenOrRecover(path);

        Assert.NotNull(quarantined);
        Assert.True(File.Exists(quarantined));
        Assert.Empty(store.ReadGames());
        store.SetSetting("theme", "{}");
        Assert.Equal("{}", store.GetSetting("theme"));
    }

    [Fact]
    public void DiagnosticsLog_AppendsAndStartsOverAtTheLimit()
    {
        string path = Path.Combine(_root, "crash.log");
        Assert.True(DiagnosticsLog.Append(path, "first\nline", limit: 64));
        Assert.True(DiagnosticsLog.Append(path, "second", limit: 64));
        Assert.Equal(["first line", "second"], File.ReadAllLines(path));

        File.WriteAllText(path, new string('x', 64));
        Assert.True(DiagnosticsLog.Append(path, "fresh", limit: 64));
        Assert.Equal(["fresh"], File.ReadAllLines(path));
    }

    [Fact]
    public void DiagnosticsLog_NeverThrowsOnBadPath() =>
        Assert.False(DiagnosticsLog.Append(Path.Combine(_root, "missing", "dir", "crash.log"), "entry"));

    [Theory]
    [InlineData(100, false, 0xE83F)]
    [InlineData(95, false, 0xE859)]
    [InlineData(4, false, 0xE850)]
    [InlineData(55, true, 0xE85F)]
    [InlineData(100, true, 0xE83E)]
    [InlineData(-5, false, 0xE850)]
    [InlineData(140, false, 0xE83F)]
    public void BatteryGlyph_TracksLevelAndCharging(int percent, bool charging, int expected) =>
        Assert.Equal(char.ConvertFromUtf32(expected), BatteryDisplay.Glyph(percent, charging));

    [Theory]
    [InlineData(0, false, 0xE863)]
    [InlineData(40, false, 0xE867)]
    [InlineData(89, false, 0xE86B)]
    [InlineData(90, false, 0xEA94)]
    [InlineData(100, false, 0xEA95)]
    [InlineData(40, true, 0xE85E)]
    public void BatteryGlyph_SaverTracksLevelOnlyWhileDischarging(int percent, bool charging, int expected) =>
        Assert.Equal(char.ConvertFromUtf32(expected), BatteryDisplay.Glyph(percent, charging, saver: true));

    [Theory]
    [InlineData(15, false, true)]
    [InlineData(16, false, false)]
    [InlineData(5, true, false)]
    public void BatteryLow_OnlyWhenDischargingAtOrBelow15(int percent, bool charging, bool low) =>
        Assert.Equal(low, BatteryDisplay.IsLow(percent, charging));

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
