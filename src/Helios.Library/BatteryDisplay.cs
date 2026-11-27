namespace Helios.Library;

/// <summary>Maps a battery reading to a Segoe Fluent Icons glyph and an accessible label for the header.</summary>
public static class BatteryDisplay
{
    private const int Battery0 = 0xE850; // Battery0..Battery9 are consecutive.
    private const int Battery10 = 0xE83F;
    private const int Charging0 = 0xE85A; // BatteryCharging0..BatteryCharging8 are consecutive.
    private const int Charging9 = 0xE83E;
    private const int Saver = 0xE863;

    public static string Glyph(int percent, bool charging, bool saver = false)
    {
        int level = Math.Clamp(percent, 0, 100) / 10; // 0..10
        int code = saver && !charging ? Saver
            : charging ? (level >= 9 ? Charging9 : Charging0 + level)
            : level >= 10 ? Battery10 : Battery0 + level;
        return char.ConvertFromUtf32(code);
    }

    public static string Label(int percent, bool charging) =>
        $"Battery {Math.Clamp(percent, 0, 100)} percent{(charging ? ", charging" : string.Empty)}";

    /// <summary>Low battery gets the warning color so a handheld player notices before the game is cut off.</summary>
    public static bool IsLow(int percent, bool charging) => !charging && percent <= 15;
}
