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
    internal string DataDirectory { get; }
    internal bool IsFullScreen => AppWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen;

    internal MainWindow(bool startedAtSignIn)
    {
        InitializeComponent();
        _handle = WindowNative.GetWindowHandle(this);
        AppWindow.SetIcon("Assets/AppIcon.ico");
        DataDirectory = GetDataDirectory();
        var dashboard = new Dashboard(this);
        Content = dashboard;
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        int width = Math.Min(1320, area.Width);
        int height = Math.Min(860, area.Height);
        AppWindow.MoveAndResize(new RectInt32(area.X + (area.Width - width) / 2, area.Y + (area.Height - height) / 2, width, height));
        SetFullScreen(startedAtSignIn || XboxMode.IsActive() == true);
        dashboard.Loaded += (_, _) =>
        {
            using var process = Process.GetCurrentProcess();
            XboxMode.WriteActivation(DataDirectory, startedAtSignIn, (long)(DateTime.Now - process.StartTime).TotalMilliseconds);
        };
    }

    internal void SetFullScreen(bool fullScreen) =>
        AppWindow.SetPresenter(fullScreen ? AppWindowPresenterKind.FullScreen : AppWindowPresenterKind.Default);

    internal void BringToFront()
    {
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
