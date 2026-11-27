using System.Runtime.InteropServices;

namespace Helios_Launcher.Windows;

internal static class NativeWindow
{
    internal static void AllowForeground(uint processId) => AllowSetForegroundWindow(processId);

    internal static void RequestForeground(nint window)
    {
        if (IsIconic(window))
            ShowWindow(window, 9); // SW_RESTORE
        SetForegroundWindow(window);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint window, int command);
}
