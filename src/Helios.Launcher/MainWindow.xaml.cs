using System.Diagnostics;
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

    /// <summary>Raised on the UI thread when Windows enters or leaves Xbox mode.</summary>
    internal event Action? XboxModeChanged;

    internal MainWindow(string activation)
    {
        InitializeComponent();
        _handle = WindowNative.GetWindowHandle(this);
        AppWindow.SetIcon("Assets/AppIcon.ico");
        AppWindow.TitleBar.PreferredTheme = TitleBarTheme.Dark;
        DataDirectory = GetDataDirectory();
        var dashboard = new Dashboard(this);
        Content = dashboard;
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        int width = Math.Min(1440, area.Width);
        int height = Math.Min(900, area.Height);
        AppWindow.MoveAndResize(new RectInt32(area.X + (area.Width - width) / 2, area.Y + (area.Height - height) / 2, width, height));
        // Xbox mode (as the gaming home or a startup app) and sign-in launches always open full screen.
        SetFullScreen(activation == "StartupTask" || XboxMode.IsActive() == true);

        var queue = DispatcherQueue;
        _xboxMode.Changed += () => queue.TryEnqueue(() => XboxModeChanged?.Invoke());
        Closed += (_, _) => _xboxMode.Dispose();
        dashboard.Loaded += (_, _) =>
        {
            using var process = Process.GetCurrentProcess();
            XboxMode.WriteActivation(DataDirectory, activation, (long)(DateTime.Now - process.StartTime).TotalMilliseconds);
        };
    }

    internal void SetFullScreen(bool fullScreen)
    {
        var kind = fullScreen ? AppWindowPresenterKind.FullScreen : AppWindowPresenterKind.Default;
        if (AppWindow.Presenter.Kind != kind)
            AppWindow.SetPresenter(kind);
    }

    internal void BringToFront()
    {
        if (XboxMode.IsActive() == true)
            SetFullScreen(true);
        Activate();
        NativeWindow.RequestForeground(_handle);
    }

    private static string GetDataDirectory()
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
