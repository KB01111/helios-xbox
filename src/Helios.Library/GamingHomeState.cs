namespace Helios.Library;

/// <summary>The per-user Xbox mode home configuration read from GamingConfiguration.</summary>
public sealed record GamingHomeState(string? HomeApp, bool? StartupToHome, bool IsHelios)
{
    public const string XboxAppId = "Microsoft.GamingApp_8wekyb3d8bbwe!Microsoft.Xbox.App";

    /// <param name="observedAsHome">
    /// True when Windows has activated Helios through windows.gaming:///home or ///library. Some builds never write
    /// GamingHomeApp, so that activation proves Helios is the home app whenever the registry value is missing.
    /// </param>
    public static GamingHomeState From(string? homeApp, bool? startupToHome, string? heliosAppId, bool observedAsHome = false)
    {
        string? home = string.IsNullOrWhiteSpace(homeApp) ? null : homeApp.Trim();
        bool isHelios = home is not null
            ? heliosAppId is not null && string.Equals(home, heliosAppId, StringComparison.OrdinalIgnoreCase)
            : observedAsHome;
        return new GamingHomeState(home, startupToHome, isHelios);
    }

    /// <summary>A short, user-facing name for the configured home app.</summary>
    public string HomeDisplayName => IsHelios ? "Helios"
        : HomeApp is null || string.Equals(HomeApp, XboxAppId, StringComparison.OrdinalIgnoreCase) ? "Xbox app"
        : HomeApp[..(HomeApp.IndexOf('_') is > 0 and var end ? end : HomeApp.Length)];
}
