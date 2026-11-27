using Helios_Launcher.Windows;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace Helios_Launcher;

public partial class App : Application
{
    private MainWindow? _window;

    public App() => InitializeComponent();

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
            var app = new App { Activation = activation.Kind.ToString() };
            primary.Activated += (_, _) => queue.TryEnqueue(() => app._window?.BringToFront());
        });
    }

    private string Activation { get; init; } = "Launch";

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow(Activation);
        _window.Activate();
    }
}
