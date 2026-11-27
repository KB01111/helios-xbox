using System.Runtime.InteropServices;

namespace Helios_Launcher.Windows;

/// <summary>
/// Puts Helios into Windows efficiency mode while a game has the foreground. As the Xbox mode home, Helios
/// stays resident for the whole session, so it should not compete with the game for CPU, power or memory.
/// </summary>
internal static class ProcessMode
{
    private const int ProcessPowerThrottling = 4;
    private const uint PowerThrottlingExecutionSpeed = 1;
    private const uint NormalPriorityClass = 0x20;
    private const uint BelowNormalPriorityClass = 0x4000;

    internal static bool IsBackground { get; private set; }

    internal static void SetBackground(bool background)
    {
        if (IsBackground == background)
            return;
        IsBackground = background;
        nint process = GetCurrentProcess();
        // EcoQoS: Windows schedules the process on efficient cores at lower clocks (Task Manager "Efficiency mode").
        var state = new PowerThrottlingState
        {
            Version = 1,
            ControlMask = PowerThrottlingExecutionSpeed,
            StateMask = background ? PowerThrottlingExecutionSpeed : 0,
        };
        SetProcessInformation(process, ProcessPowerThrottling, ref state, (uint)Marshal.SizeOf<PowerThrottlingState>());
        SetPriorityClass(process, background ? BelowNormalPriorityClass : NormalPriorityClass);
        if (!background)
            return;
        GC.Collect(2, GCCollectionMode.Optimized, blocking: false, compacting: true);
        // Hand idle pages back to the game; Windows faults them back in when Helios returns to the front.
        SetProcessWorkingSetSize(process, -1, -1);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PowerThrottlingState
    {
        public uint Version;
        public uint ControlMask;
        public uint StateMask;
    }

    [DllImport("kernel32.dll")]
    private static extern nint GetCurrentProcess();

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessInformation(nint process, int informationClass, ref PowerThrottlingState information, uint size);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetPriorityClass(nint process, uint priorityClass);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessWorkingSetSize(nint process, nint minimum, nint maximum);
}
