using System.Diagnostics;
using System.Runtime.InteropServices;
using Helios.Library;
using Microsoft.Win32;

namespace Helios_Launcher.Windows;

/// <summary>
/// Wraps the Windows Xbox mode (gaming full screen experience) API set and the per-user home app configuration.
/// Every call degrades to "unknown" on builds that do not implement api-ms-win-gaming-experience-l1-1-0.
/// </summary>
internal sealed class XboxMode : IDisposable
{
    private const string GamingApi = "api-ms-win-gaming-experience-l1-1-0.dll";
    private const string ConfigurationKey = @"Software\Microsoft\Windows\CurrentVersion\GamingConfiguration";

    private readonly ChangeRoutine _routine;
    private nint _registration;

    internal event Action? Changed;

    internal XboxMode()
    {
        _routine = _ => Changed?.Invoke();
        try
        {
            if (RegisterGamingFullScreenExperienceChangeNotification(_routine, 0, out _registration) < 0)
                _registration = 0;
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException)
        {
            _registration = 0;
        }
    }

    internal static bool? IsActive() => Call(IsGamingFullScreenExperienceActive);

    internal static bool? IsSupported() => Call(IsGamingFullScreenExperienceSupported);

    internal static bool CanSet() => Call(CanSetGamingFullScreenExperience) == true;

    internal enum SetResult { Done, AppliesAfterRestart, Declined }

    /// <summary>Enters or leaves Xbox mode.</summary>
    internal static SetResult Set(bool active)
    {
        try
        {
            int result = SetGamingFullScreenExperience(active);
            // ERROR_FAIL_NOACTION_REBOOT: Windows accepted the change and applies it on restart. Handhelds that start
            // in Xbox mode offer a restart when re-entering, to bring back the background-process limits.
            return result == unchecked((int)0x8007015E) ? SetResult.AppliesAfterRestart
                : result >= 0 ? SetResult.Done : SetResult.Declined;
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException)
        {
            return SetResult.Declined;
        }
    }

    /// <summary>Enters or leaves Xbox mode. Returns false when Windows declined the request.</summary>
    internal static bool TrySet(bool active) => Set(active) != SetResult.Declined;

    /// <summary>The app user model ID Windows uses for this package, or null when running without package identity.</summary>
    internal static string? AppUserModelId
    {
        get
        {
            try
            {
                return global::Windows.ApplicationModel.Package.Current.Id.FamilyName + "!App";
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }
    }

    private const string ObservedHomeFile = "gaming-home.seen";

    /// <summary>True once Windows has activated Helios through Game Bar Home or Library (windows.gaming:///...).</summary>
    internal static bool ObservedAsHome { get; private set; }

    internal static void LoadObservedHome(string dataDirectory) =>
        ObservedAsHome = File.Exists(Path.Combine(dataDirectory, ObservedHomeFile));

    internal static void RecordObservedHome(string dataDirectory)
    {
        if (ObservedAsHome)
            return;
        ObservedAsHome = true;
        DiagnosticsLog.Append(Path.Combine(dataDirectory, ObservedHomeFile), DateTimeOffset.UtcNow.ToString("O"));
    }

    internal static GamingHomeState ReadHome()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(ConfigurationKey);
            return GamingHomeState.From(
                key?.GetValue("GamingHomeApp") as string,
                key?.GetValue("StartupToGamingHome") is int startup ? startup != 0 : null,
                AppUserModelId,
                ObservedAsHome);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return GamingHomeState.From(null, null, AppUserModelId, ObservedAsHome);
        }
    }

    internal static void WriteActivation(string dataDirectory, string activation, long elapsedMilliseconds)
    {
        string state = IsActive() is { } active ? (active ? "active" : "inactive") : "unknown";
        var home = ReadHome();
        string entry = $"{DateTimeOffset.UtcNow:O} activation={activation} xboxMode={state} "
            + $"home={(home.IsHelios ? "helios" : home.HomeApp ?? "unset")} startupToHome={home.StartupToHome?.ToString() ?? "unset"} "
            + $"windowMs={elapsedMilliseconds}";
        if (!DiagnosticsLog.Append(Path.Combine(dataDirectory, "activation.log"), entry))
            Debug.WriteLine("activation.log could not be written");
    }

    public void Dispose()
    {
        if (_registration == 0)
            return;
        UnregisterGamingFullScreenExperienceChangeNotification(_registration);
        _registration = 0;
        GC.KeepAlive(_routine);
    }

    private static bool? Call(Func<bool> api)
    {
        try
        {
            return api();
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void ChangeRoutine(nint context);

    [DllImport(GamingApi)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsGamingFullScreenExperienceActive();

    [DllImport(GamingApi)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsGamingFullScreenExperienceSupported();

    [DllImport(GamingApi)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CanSetGamingFullScreenExperience();

    [DllImport(GamingApi)]
    private static extern int SetGamingFullScreenExperience([MarshalAs(UnmanagedType.Bool)] bool active);

    [DllImport(GamingApi)]
    private static extern int RegisterGamingFullScreenExperienceChangeNotification(ChangeRoutine routine, nint context, out nint registration);

    [DllImport(GamingApi)]
    private static extern void UnregisterGamingFullScreenExperienceChangeNotification(nint registration);
}
