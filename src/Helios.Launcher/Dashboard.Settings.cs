using Helios_Launcher.Windows;
using Microsoft.Data.Sqlite;
using Microsoft.UI.Xaml;
using Windows.ApplicationModel;

namespace Helios_Launcher;

public sealed partial class Dashboard
{
    private bool _updatingSettings;
    private const string StartupId = "HeliosSignIn";

    private async Task RefreshStartupAsync()
    {
        if (_updatingSettings)
            return;
        _updatingSettings = true;
        FullScreenToggle.IsOn = _window.IsFullScreen;
        try
        {
            RenderStartup(await StartupTask.GetAsync(StartupId));
        }
        catch (Exception error) when (IsLocalError(error))
        {
            StartupToggle.IsEnabled = false;
            StartupMessage.Text = "Install the MSIX package to use launch at sign-in.";
        }
        finally
        {
            _updatingSettings = false;
        }
        RenderXboxMode();
    }

    private void RenderXboxMode()
    {
        bool? active = XboxMode.IsActive();
        bool? supported = XboxMode.IsSupported();
        var home = XboxMode.ReadHome();
        bool canSet = XboxMode.CanSet();

        ModeState.Text = active switch
        {
            true => "On",
            false when supported == false => "Not available on this PC",
            false => "Off",
            null => "Not reported by this Windows build",
        };
        HomeState.Text = home.IsHelios ? "Helios ✓" : home.HomeDisplayName;
        SignInState.Text = home.StartupToHome switch
        {
            true => "Starts in Xbox mode",
            false => "Starts on the desktop",
            null => "Not configured",
        };
        ModeMessage.Text = !home.IsHelios
            ? "To boot into Helios instead of the Xbox app, choose Helios under Choose home app, then turn on Enter Xbox mode on startup. "
              + "If Helios is missing from the list, reinstall it with scripts/Install-Helios.ps1, which installs the gaming home capability."
            : home.StartupToHome != true
                ? "Helios is the Xbox mode home. Turn on Enter Xbox mode on startup to boot straight into Helios."
                : "Helios opens when Windows starts in Xbox mode and when you press Home in Game Bar.";
        ModeSwitchButton.Content = active == true ? "Return to desktop" : "Enter Xbox mode";
        ModeSwitchButton.IsEnabled = active == true || canSet;
        AboutText.Text = $"Helios Launcher {AppVersion()} · {XboxMode.AppUserModelId ?? "running without package identity"}";
    }

    private static string AppVersion()
    {
        try
        {
            var version = Package.Current.Id.Version;
            return $"{version.Major}.{version.Minor}.{version.Build}";
        }
        catch (InvalidOperationException)
        {
            return "dev";
        }
    }

    private void ModeSwitchClicked(object sender, RoutedEventArgs args)
    {
        bool enter = XboxMode.IsActive() != true;
        if (!XboxMode.TrySet(enter))
            Status.Text = enter ? "Windows did not enter Xbox mode. Check Xbox mode settings." : "Windows did not leave Xbox mode.";
        RenderXboxMode();
    }

    private void RenderStartup(StartupTask task)
    {
        StartupToggle.IsOn = task.State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
        StartupToggle.IsEnabled = task.State is StartupTaskState.Enabled or StartupTaskState.Disabled;
        StartupMessage.Text = task.State switch
        {
            StartupTaskState.DisabledByUser => "Helios was turned off in Windows Startup settings. Turn it back on there.",
            StartupTaskState.DisabledByPolicy or StartupTaskState.EnabledByPolicy => "Windows policy controls this startup setting.",
            _ => "Opens Helios full screen after a desktop sign-in. When Helios is the Xbox mode home, Windows starts it for you.",
        };
    }

    private async void StartupChanged(object sender, RoutedEventArgs args)
    {
        if (_updatingSettings)
            return;
        _updatingSettings = true;
        StartupToggle.IsEnabled = false;
        try
        {
            var task = await StartupTask.GetAsync(StartupId);
            if (StartupToggle.IsOn)
                await task.RequestEnableAsync();
            else
                task.Disable();
            RenderStartup(task);
        }
        catch (Exception error) when (IsLocalError(error))
        {
            StartupMessage.Text = $"Could not change startup. {error.Message}";
        }
        finally
        {
            _updatingSettings = false;
        }
    }

    private void ApplyFullScreen(bool enabled)
    {
        _window.SetFullScreen(enabled);
        try
        {
            _store?.SetSetting("fullScreen", enabled ? "true" : "false");
        }
        catch (SqliteException error)
        {
            Status.Text = $"The display setting could not be saved. {error.Message}";
        }
    }

    private void FullScreenChanged(object sender, RoutedEventArgs args)
    {
        if (!_updatingSettings)
            ApplyFullScreen(FullScreenToggle.IsOn);
    }

    private void OpenModeClicked(object sender, RoutedEventArgs args) => OpenSettings("ms-settings:gaming-fullscreen");
    private void OpenStartupClicked(object sender, RoutedEventArgs args) => OpenSettings("ms-settings:startupapps");

    private void OpenSettings(string uri)
    {
        try
        {
            GameLaunch.Open(uri);
        }
        catch (Exception error) when (IsLocalError(error))
        {
            Status.Text = $"Could not open Windows settings. {error.Message}";
        }
    }
}
