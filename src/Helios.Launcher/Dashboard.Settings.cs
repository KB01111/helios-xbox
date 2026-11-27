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
        ModeMessage.Text = XboxMode.IsActive() switch
        {
            true => "Xbox mode is active. Use Game Bar or Task View to return to the Windows desktop.",
            false => "Xbox mode is inactive. Open Windows Settings to see the gaming home and startup options available on this PC.",
            null => "Windows did not report the Xbox mode state. Check availability in Windows Settings.",
        };
    }

    private void RenderStartup(StartupTask task)
    {
        StartupToggle.IsOn = task.State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
        StartupToggle.IsEnabled = task.State is StartupTaskState.Enabled or StartupTaskState.Disabled;
        StartupMessage.Text = task.State switch
        {
            StartupTaskState.DisabledByUser => "Helios was turned off in Windows Startup settings. Turn it back on there.",
            StartupTaskState.DisabledByPolicy or StartupTaskState.EnabledByPolicy => "Windows policy controls this startup setting.",
            _ => "Opens the dashboard full screen after Windows sign-in. Xbox mode can defer startup apps until the desktop opens.",
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
