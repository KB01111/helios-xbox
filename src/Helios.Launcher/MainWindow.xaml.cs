using System.Diagnostics;
using Helios.Library;
using Helios_Launcher.Windows;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Storage;
using Windows.Graphics;
using WinRT.Interop;

namespace Helios_Launcher;

public sealed partial class MainWindow : Window
{
    private readonly nint _handle;
    private readonly XboxMode _xboxMode = new();
    internal string DataDirectory { get; }
    internal bool IsFullScreen => AppWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen;

    private bool _closeAllowed;

    /// <summary>Raised on the UI thread when Windows enters or leaves Xbox mode.</summary>
    internal event Action? XboxModeChanged;

    /// <summary>
    /// Raised when something (Alt+F4, the taskbar) tries to close Helios while it is the Xbox mode home. The close is
    /// cancelled; the dashboard offers the desktop or power options instead of leaving an empty session.
    /// </summary>
    internal event Action? CloseBlocked;

    internal MainWindow(LaunchRequest launch)
    {
        InitializeComponent();
        _handle = WindowNative.GetWindowHandle(this);
        AppWindow.SetIcon("Assets/AppIcon.ico");
        AppWindow.TitleBar.PreferredTheme = TitleBarTheme.Dark;
        DataDirectory = GetDataDirectory();
        XboxMode.LoadObservedHome(DataDirectory);
        if (launch.FromGamingShell)
            XboxMode.RecordObservedHome(DataDirectory);

        // Read the display mode and theme before the first frame, so Helios opens full screen in its own colors.
        var early = LibraryStore.PeekSettings(Path.Combine(DataDirectory, "library.db"), "fullScreen", "theme");
        bool desktopFullScreen = early.GetValueOrDefault("fullScreen") != "false"; // Full screen unless turned off.
        var dashboard = new Dashboard(this, launch, ConsoleTheme.FromJson(early.GetValueOrDefault("theme")), desktopFullScreen);
        Content = dashboard;

        // The windowed size is what F11 or the Settings toggle returns to on the desktop.
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        int width = Math.Min(1440, area.Width);
        int height = Math.Min(900, area.Height);
        AppWindow.MoveAndResize(new RectInt32(area.X + (area.Width - width) / 2, area.Y + (area.Height - height) / 2, width, height));
        // Helios is a console-style home, so it starts full screen. Xbox mode, Game Bar and sign-in launches always do.
        SetFullScreen(desktopFullScreen || launch.FromStartupTask || launch.FromGamingShell || XboxMode.IsActive() == true);
        Power.SetRestartOnCrash(XboxMode.IsActive() == true);

        var queue = DispatcherQueue;
        _xboxMode.Changed += () => queue.TryEnqueue(() =>
        {
            Power.SetRestartOnCrash(XboxMode.IsActive() == true);
            XboxModeChanged?.Invoke();
        });
        AppWindow.Closing += (_, args) =>
        {
            if (_closeAllowed || XboxMode.IsActive() != true)
                return;
            args.Cancel = true;
            CloseBlocked?.Invoke();
        };
        Closed += (_, _) =>
        {
            Power.SetRestartOnCrash(false); // A deliberate exit must not bring Helios back.
            _xboxMode.Dispose();
        };
        dashboard.Loaded += (_, _) =>
        {
            using var process = Process.GetCurrentProcess();
            XboxMode.WriteActivation(DataDirectory, launch.Describe(), (long)(DateTime.Now - process.StartTime).TotalMilliseconds);
        };
    }

    /// <summary>Closes Helios even in Xbox mode. Only call after the player confirmed it.</summary>
    internal void CloseForReal()
    {
        _closeAllowed = true;
        Close();
    }

    /// <summary>Sets the window mode. Xbox mode always wins: the home app never leaves full screen there.</summary>
    internal void SetFullScreen(bool fullScreen)
    {
        if (XboxMode.IsActive() == true)
            fullScreen = true;
        var kind = fullScreen ? AppWindowPresenterKind.FullScreen : AppWindowPresenterKind.Default;
        if (AppWindow.Presenter.Kind != kind)
            AppWindow.SetPresenter(kind);
    }

    /// <summary>Handles a second activation: Game Bar Home or Library, the Start menu, or helios-launcher://.</summary>
    internal void BringToFront(LaunchRequest launch)
    {
        if (launch.FromGamingShell)
            XboxMode.RecordObservedHome(DataDirectory);
        if (XboxMode.IsActive() == true || launch.FromGamingShell)
            SetFullScreen(true);
        Activate();
        NativeWindow.RequestForeground(_handle);
        (Content as Dashboard)?.ShowRequested(launch);
        XboxMode.WriteActivation(DataDirectory, launch.Describe() + "+redirect", 0);
    }

    /// <summary>Shows an unexpected error in the status bar instead of closing the Xbox mode home.</summary>
    internal void ReportError(Exception error) => (Content as Dashboard)?.ShowError(error);

    internal static string GetDataDirectory()
    {
        try
        {
            return ApplicationData.Current.LocalFolder.Path;
        }
        catch (InvalidOperationException)
        {
            string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HeliosLauncher");
            Directory.CreateDirectory(directory);
            return directory;
        }
    }
}
