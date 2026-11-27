using Helios.Library;
using Helios_Launcher.Windows;
using Microsoft.Data.Sqlite;
using Microsoft.UI.Xaml;
using Windows.ApplicationModel;

namespace Helios_Launcher;

public sealed partial class Dashboard
{
    private bool _updatingSettings;
    private bool _fullScreenPreferencePending;
    private bool _gamingShellActivatedWhileLoading;
    private const string StartupId = "HeliosSignIn";

    private async Task RefreshStartupAsync()
    {
        if (_updatingSettings)
            return;
        _updatingSettings = true;
        // The toggle is the desktop preference; Xbox mode always runs full screen regardless.
        FullScreenToggle.IsOn = DesktopFullScreenPreference;
        FullScreenMessage.Visibility = Visible(XboxMode.IsActive() == true);
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
        // Wording follows Microsoft's Xbox mode guide: handhelds pick a home app; other PCs turn on Enable Xbox mode.
        ModeMessage.Text = !home.IsHelios
            ? "On a gaming handheld, choose Helios under Choose home app, then turn on Enter Xbox mode on startup. "
              + "On other PCs, turn on Enable Xbox mode, and Show Xbox mode in Task View if you want it there. "
              + "If Helios is missing from Choose home app, reinstall it with scripts/Install-Helios.ps1, which adds the gaming home capability."
            : home.StartupToHome != true
                ? "Helios is the Xbox mode home: Home in Game Bar returns here and Library opens your games. "
                  + "Turn on Enter Xbox mode on startup to boot straight into Helios."
                : "Helios opens when Windows starts in Xbox mode. Home in Game Bar returns here, Library opens your games, "
                  + "and Win+F11 switches between Xbox mode and the desktop.";
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
        Status.Text = XboxMode.Set(enter) switch
        {
            XboxMode.SetResult.AppliesAfterRestart => enter
                ? "Windows will start Xbox mode after a restart, with background apps limited for gaming."
                : "Windows will return to the desktop after a restart.",
            XboxMode.SetResult.Declined => enter
                ? "Windows did not enter Xbox mode. Check Settings > Gaming > Xbox mode, or press Win+F11."
                : "Windows did not leave Xbox mode. Use Game Bar, Task View or Win+F11.",
            _ => Status.Text,
        };
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
            _ => "Opens Helios full screen after a desktop sign-in. In Xbox mode, Windows starts Helios as the home app. "
                 + "Settings > Apps > Startup also offers Start when exiting to desktop for your other apps.",
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

    /// <summary>Full screen on the desktop, as saved. Loaded once with the library so input never waits on SQLite.</summary>
    private bool DesktopFullScreenPreference { get; set; }

    private void LoadFullScreenPreference(LibraryStore store)
    {
        if (_fullScreenPreferencePending)
        {
            _fullScreenPreferencePending = false;
            SaveFullScreenPreference(store);
            return;
        }

        DesktopFullScreenPreference = store.GetSetting("fullScreen") != "false";
        _window.SetFullScreen(DesktopFullScreenPreference || _launch.FromStartupTask
            || _launch.FromGamingShell || _gamingShellActivatedWhileLoading);

        bool updating = _updatingSettings;
        _updatingSettings = true;
        FullScreenToggle.IsOn = DesktopFullScreenPreference;
        _updatingSettings = updating;
    }

    private void ApplyFullScreen(bool enabled)
    {
        DesktopFullScreenPreference = enabled;
        _window.SetFullScreen(enabled); // Ignored in Xbox mode, which stays full screen.
        if (_store is { } store)
            SaveFullScreenPreference(store);
        else
            _fullScreenPreferencePending = true;
    }

    private void SaveFullScreenPreference(LibraryStore store)
    {
        try
        {
            store.SetSetting("fullScreen", DesktopFullScreenPreference ? "true" : "false");
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
