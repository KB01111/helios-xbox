namespace Helios.Library;

/// <summary>The per-user Xbox mode home configuration read from GamingConfiguration.</summary>
public sealed record GamingHomeState(string? HomeApp, bool? StartupToHome, bool IsHelios)
{
    public const string XboxAppId = "Microsoft.GamingApp_8wekyb3d8bbwe!Microsoft.Xbox.App";

    public static GamingHomeState From(string? homeApp, bool? startupToHome, string? heliosAppId)
    {
        string? home = string.IsNullOrWhiteSpace(homeApp) ? null : homeApp.Trim();
        bool isHelios = home is not null && heliosAppId is not null
            && string.Equals(home, heliosAppId, StringComparison.OrdinalIgnoreCase);
        return new GamingHomeState(home, startupToHome, isHelios);
    }

    /// <summary>A short, user-facing name for the configured home app.</summary>
    public string HomeDisplayName => IsHelios ? "Helios"
        : HomeApp is null || string.Equals(HomeApp, XboxAppId, StringComparison.OrdinalIgnoreCase) ? "Xbox app"
        : HomeApp[..(HomeApp.IndexOf('_') is > 0 and var end ? end : HomeApp.Length)];
}
