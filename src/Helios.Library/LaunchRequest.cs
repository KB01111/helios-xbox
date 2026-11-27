namespace Helios.Library;

public enum LaunchTarget { Home, Library }

/// <summary>
/// Why Windows started or re-activated Helios. In Xbox mode, Game Bar's Home and Library buttons activate the home
/// app with windows.gaming:///home and windows.gaming:///library (the URIs GameBar.exe and twinui.pcshell.dll use).
/// </summary>
public sealed record LaunchRequest(string Kind, LaunchTarget Target, bool FromGamingShell, bool FromStartupTask)
{
    public const string GamingScheme = "windows.gaming";
    public const string HeliosScheme = "helios-launcher";

    public static LaunchRequest Default { get; } = new("Launch", LaunchTarget.Home, false, false);

    public static LaunchRequest Parse(string? kind, string? uri)
    {
        kind = string.IsNullOrWhiteSpace(kind) ? "Launch" : kind;
        bool startup = kind == "StartupTask";
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed))
            return new LaunchRequest(kind, LaunchTarget.Home, false, startup);

        bool gaming = parsed.Scheme.Equals(GamingScheme, StringComparison.OrdinalIgnoreCase);
        if (!gaming && !parsed.Scheme.Equals(HeliosScheme, StringComparison.OrdinalIgnoreCase))
            return new LaunchRequest(kind, LaunchTarget.Home, false, startup);

        // windows.gaming:///library carries the page in the path; helios-launcher://library carries it in the host.
        string page = (parsed.Host + parsed.AbsolutePath).Trim('/');
        var target = page.Equals("library", StringComparison.OrdinalIgnoreCase) ? LaunchTarget.Library : LaunchTarget.Home;
        return new LaunchRequest(kind, target, gaming, startup);
    }

    /// <summary>Short form for activation.log, for example "Protocol:gaming/library".</summary>
    public string Describe() => FromGamingShell || Kind == "Protocol"
        ? $"{Kind}:{(FromGamingShell ? "gaming" : "helios")}/{Target.ToString().ToLowerInvariant()}"
        : Kind;
}
