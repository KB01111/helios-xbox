using System.Runtime.InteropServices;

namespace Helios_Launcher.Windows;

internal enum PowerAction { Sleep, Restart, ShutDown, SignOut }

/// <summary>
/// Console-style power controls for the Xbox mode home, plus Windows Error Reporting restart registration so a
/// crash in Xbox mode brings Helios back instead of leaving an empty session.
/// </summary>
internal static class Power
{
    private const uint EwxLogoff = 0x0;
    private const uint EwxReboot = 0x2;
    private const uint EwxPowerOff = 0x8;
    private const uint EwxHybridShutdown = 0x400000;
    private const uint ShutdownReasonPlanned = 0x80000000; // SHTDN_REASON_FLAG_PLANNED | MAJOR_OTHER | MINOR_OTHER
    private const uint RestartNoReboot = 0x8;
    private const int ScMonitorPower = 0xF170;
    private const uint WmSysCommand = 0x0112;
    private static readonly nint Broadcast = 0xFFFF;

    /// <summary>Runs a power action. Returns false when Windows refused it.</summary>
    internal static bool Run(PowerAction action) => action switch
    {
        PowerAction.Sleep => Sleep(),
        PowerAction.Restart => Exit(EwxReboot),
        // Hybrid shutdown matches Start > Shut down when Fast Startup is on; fall back to a full shutdown.
        PowerAction.ShutDown => Exit(EwxPowerOff | EwxHybridShutdown) || Exit(EwxPowerOff),
        _ => ExitWindowsEx(EwxLogoff, ShutdownReasonPlanned),
    };

    private static bool Sleep()
    {
        // Handhelds use Modern Standby (S0 low power idle), where SetSuspendState does nothing. Turning the
        // display off is how Windows enters standby there, the same as pressing the power button.
        if (!IsModernStandby() && EnableShutdownPrivilege() && SetSuspendState(false, false, false))
            return true;
        PostMessage(Broadcast, WmSysCommand, ScMonitorPower, 2);
        return true;
    }

    private static bool Exit(uint flags) => EnableShutdownPrivilege() && ExitWindowsEx(flags, ShutdownReasonPlanned);

    private static bool IsModernStandby()
    {
        // SYSTEM_POWER_CAPABILITIES: AoAc (always on, always connected) is the BOOLEAN at offset 20.
        var capabilities = new byte[128];
        return GetPwrCapabilities(capabilities) && capabilities[20] != 0;
    }

    private static bool EnableShutdownPrivilege()
    {
        if (!OpenProcessToken(GetCurrentProcess(), 0x28 /* TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY */, out nint token))
            return false;
        try
        {
            if (!LookupPrivilegeValue(null, "SeShutdownPrivilege", out long luid))
                return false;
            var privileges = new TokenPrivileges { Count = 1, Luid = luid, Attributes = 0x2 /* SE_PRIVILEGE_ENABLED */ };
            return AdjustTokenPrivileges(token, false, ref privileges, 0, 0, 0) && Marshal.GetLastWin32Error() == 0;
        }
        finally
        {
            CloseHandle(token);
        }
    }

    /// <summary>Asks Windows to relaunch Helios after a crash or hang. Normal exits are never restarted.</summary>
    internal static void SetRestartOnCrash(bool enabled)
    {
        try
        {
            if (enabled)
                RegisterApplicationRestart("--restarted", RestartNoReboot);
            else
                UnregisterApplicationRestart();
        }
        catch (EntryPointNotFoundException)
        {
            // Not available; the session falls back to Game Bar Home.
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct TokenPrivileges
    {
        public uint Count;
        public long Luid;
        public uint Attributes;
    }

    [DllImport("kernel32.dll")]
    private static extern nint GetCurrentProcess();

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegisterApplicationRestart(string commandLine, uint flags);

    [DllImport("kernel32.dll")]
    private static extern int UnregisterApplicationRestart();

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(nint process, uint access, out nint token);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LookupPrivilegeValue(string? system, string name, out long luid);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AdjustTokenPrivileges(nint token, [MarshalAs(UnmanagedType.Bool)] bool disableAll,
        ref TokenPrivileges state, uint length, nint previous, nint returnLength);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ExitWindowsEx(uint flags, uint reason);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(nint window, uint message, nint wParam, nint lParam);

    [DllImport("powrprof.dll")]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool SetSuspendState([MarshalAs(UnmanagedType.U1)] bool hibernate,
        [MarshalAs(UnmanagedType.U1)] bool force, [MarshalAs(UnmanagedType.U1)] bool disableWakeEvent);

    [DllImport("powrprof.dll")]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool GetPwrCapabilities([Out] byte[] capabilities);
}
