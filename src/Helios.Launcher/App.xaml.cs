using Helios.Library;
using Helios_Launcher.Windows;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace Helios_Launcher;

public partial class App : Application
{
    private MainWindow? _window;

    public App()
    {
        InitializeComponent();
        // As the Xbox mode home, a crash leaves an empty session, so log and keep running whenever the UI thread allows.
        UnhandledException += (_, args) =>
        {
            LogCrash("ui", args.Exception);
            args.Handled = true;
            _window?.ReportError(args.Exception);
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            LogCrash("task", args.Exception);
            args.SetObserved();
        };
        // Background-thread crashes cannot be stopped; the log and restart registration (Power) cover them.
        AppDomain.CurrentDomain.UnhandledException += (_, args) => LogCrash("fatal", args.ExceptionObject as Exception);
    }

    private static void LogCrash(string kind, Exception? error)
    {
        try
        {
            DiagnosticsLog.Append(Path.Combine(MainWindow.GetDataDirectory(), "crash.log"),
                $"{DateTimeOffset.UtcNow:O} {kind} {error?.GetType().FullName}: {error?.Message} | {error?.StackTrace}", 128 * 1024);
        }
        catch (Exception logging) when (logging is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            System.Diagnostics.Debug.WriteLine(logging.Message);
        }
    }

    [STAThread]
    private static void Main()
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
        var primary = AppInstance.FindOrRegisterForKey("HeliosLauncher");
        if (!primary.IsCurrent)
        {
            NativeWindow.AllowForeground(primary.ProcessId);
            Task.Run(async () => await primary.RedirectActivationToAsync(activation)).GetAwaiter().GetResult();
            return;
        }

        Start(_ =>
        {
            var queue = DispatcherQueue.GetForCurrentThread();
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(queue));
            var app = new App { Launch = ReadLaunch(activation) };
            primary.Activated += (_, args) =>
            {
                var launch = ReadLaunch(args);
                queue.TryEnqueue(() => app._window?.BringToFront(launch));
            };
        });
    }

    private LaunchRequest Launch { get; init; } = LaunchRequest.Default;

    /// <summary>Game Bar Home and Library arrive as protocol activations (windows.gaming:///home or ///library).</summary>
    private static LaunchRequest ReadLaunch(AppActivationArguments args) => LaunchRequest.Parse(
        args.Kind.ToString(),
        (args.Data as global::Windows.ApplicationModel.Activation.IProtocolActivatedEventArgs)?.Uri?.OriginalString);

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow(Launch);
        _window.Activate();
    }
}
