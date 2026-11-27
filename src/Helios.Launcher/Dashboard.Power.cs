using Helios.Library;
using Helios_Launcher.Windows;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.Windows.System.Power;
using PowerManager = Microsoft.Windows.System.Power.PowerManager;

namespace Helios_Launcher;

/// <summary>
/// Console shell duties for the Xbox mode home: the power menu, the battery readout for handhelds, and recovering
/// cleanly from sleep, unexpected errors and close requests.
/// </summary>
public sealed partial class Dashboard
{
    private sealed record PowerChoice(string Glyph, string Label, Action Run, bool Accent = false);

    private Control? _powerReturnFocus;
    private bool _windowActive;

    private bool PowerMenuOpen => PowerOverlay.Visibility == Visibility.Visible;

    private void InitializeShell()
    {
        _window.CloseBlocked += () => OpenPowerMenu(fromClose: true);
        PowerManager.BatteryStatusChanged += PowerStateChanged;
        PowerManager.PowerSupplyStatusChanged += PowerStateChanged;
        PowerManager.RemainingChargePercentChanged += PowerStateChanged;
        PowerManager.EnergySaverStatusChanged += PowerStateChanged;
        PowerManager.SystemSuspendStatusChanged += SuspendStatusChanged;
        _window.Closed += (_, _) =>
        {
            PowerManager.BatteryStatusChanged -= PowerStateChanged;
            PowerManager.PowerSupplyStatusChanged -= PowerStateChanged;
            PowerManager.RemainingChargePercentChanged -= PowerStateChanged;
            PowerManager.EnergySaverStatusChanged -= PowerStateChanged;
            PowerManager.SystemSuspendStatusChanged -= SuspendStatusChanged;
        };
        UpdateBattery();
    }

    // ---- Battery ----

    private void PowerStateChanged(object? sender, object args) => DispatcherQueue.TryEnqueue(UpdateBattery);

    private void UpdateBattery()
    {
        try
        {
            var status = PowerManager.BatteryStatus;
            if (status == BatteryStatus.NotPresent)
            {
                Battery.Visibility = Visibility.Collapsed;
                return;
            }
            int percent = PowerManager.RemainingChargePercent;
            bool charging = status == BatteryStatus.Charging
                || (status != BatteryStatus.Discharging && PowerManager.PowerSupplyStatus == PowerSupplyStatus.Adequate);
            bool saver = PowerManager.EnergySaverStatus == EnergySaverStatus.On;
            BatteryGlyph.Glyph = BatteryDisplay.Glyph(percent, charging, saver);
            BatteryText.Text = $"{Math.Clamp(percent, 0, 100)}%";
            var brush = BatteryDisplay.IsLow(percent, charging)
                ? new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 255, 112, 96))
                : ThemeBrush("TextFillColorPrimaryBrush");
            BatteryGlyph.Foreground = brush;
            BatteryText.Foreground = brush;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(Battery, BatteryDisplay.Label(percent, charging));
            Battery.Visibility = Visible(_size.Width >= 700);
        }
        catch (Exception error) when (error is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            Battery.Visibility = Visibility.Collapsed;
        }
    }

    // ---- Sleep and resume ----

    private void SuspendStatusChanged(object? sender, object args)
    {
        var status = PowerManager.SystemSuspendStatus;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (status == SystemSuspendStatus.Entering)
            {
                // Forget held buttons: the press that put the PC to sleep must not act on wake.
                _controller.SetActive(false);
                return;
            }
            if (status is not (SystemSuspendStatus.AutoResume or SystemSuspendStatus.ManualResume))
                return;
            UpdateClock();
            UpdateBattery();
            UpdateXboxChrome();
            // Displays can change while asleep (dock, TV); Xbox mode must come back full screen.
            _window.SetFullScreen(_window.IsFullScreen);
            if (_windowActive)
                _controller.SetActive(true);
            // Drives and installs may have changed while the PC slept.
            if (status == SystemSuspendStatus.ManualResume)
                _ = RefreshLibraryAsync();
        });
    }

    // ---- Errors ----

    internal void ShowError(Exception error) =>
        Status.Text = $"Something went wrong, but Helios kept running. {error.Message}";

    // ---- Power menu ----

    private void PowerClicked(object sender, RoutedEventArgs args) => OpenPowerMenu(fromClose: false);

    private void OpenPowerMenu(bool fromClose)
    {
        if (!PowerMenuOpen)
            _powerReturnFocus = FocusManager.GetFocusedElement(XamlRoot) as Control;
        bool xbox = XboxMode.IsActive() == true;
        var choices = new List<PowerChoice>();
        if (xbox)
            choices.Add(new("", "Return to desktop", ReturnToDesktop, Accent: fromClose));
        choices.Add(new("", "Sleep", () => RunPower(PowerAction.Sleep, "Going to sleep…")));
        choices.Add(new("", "Restart", () => Confirm("Restart now?", PowerAction.Restart, "Restart", "Restarting…")));
        choices.Add(new("", "Shut down", () => Confirm("Shut down now?", PowerAction.ShutDown, "Shut down", "Shutting down…")));
        choices.Add(new("", "Sign out", () => Confirm("Sign out now?", PowerAction.SignOut, "Sign out", "Signing out…")));
        choices.Add(xbox
            ? new("", "Close Helios", ConfirmClose)
            : new("", "Exit Helios", ExitFromPowerMenu));
        choices.Add(new("", "Cancel", ClosePowerMenu));
        ShowPowerCard(fromClose && xbox ? "Leave Helios?" : "Power",
            fromClose && xbox ? "Helios is your Xbox mode home. Return to the desktop to keep it ready for Game Bar Home." : null,
            choices);
    }

    private void Confirm(string title, PowerAction action, string label, string progress) =>
        ShowPowerCard(title, "Save your progress in any open game first. Unsaved work in other apps may be lost.",
            [new("", label, () => RunPower(action, progress), Accent: true), new("", "Back", () => OpenPowerMenu(false))]);

    private void ConfirmClose() =>
        ShowPowerCard("Close Helios?",
            "Xbox mode will have no home screen until you press Home in Game Bar, which starts Helios again.",
            [new("", "Close Helios", () => _window.CloseForReal(), Accent: true), new("", "Back", () => OpenPowerMenu(false))]);

    private void ExitFromPowerMenu()
    {
        if (XboxMode.IsActive() == true)
            ConfirmClose();
        else
            _window.CloseForReal();
    }

    private void ShowPowerCard(string title, string? message, IReadOnlyList<PowerChoice> choices)
    {
        PowerTitle.Text = title;
        PowerMessage.Text = message ?? string.Empty;
        PowerMessage.Visibility = Visible(message is not null);
        PowerChoices.Children.Clear();
        var pill = (Style)Application.Current.Resources["HeliosPill"];
        var accent = (Style)Application.Current.Resources["HeliosAccentPill"];
        foreach (var choice in choices)
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            content.Children.Add(new FontIcon { Glyph = choice.Glyph, FontSize = 16 });
            content.Children.Add(new TextBlock { Text = choice.Label });
            var button = new Button
            {
                Content = content,
                Style = choice.Accent ? accent : pill,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                MinHeight = 50,
                CornerRadius = new CornerRadius(14),
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, choice.Label);
            button.Click += (_, _) => choice.Run();
            PowerChoices.Children.Add(button);
        }
        PowerOverlay.Visibility = Visibility.Visible;
        UpdateHints();
        DispatcherQueue.TryEnqueue(FocusPowerMenu);
    }

    private void FocusPowerMenu()
    {
        UpdateLayout();
        (PowerChoices.Children.FirstOrDefault() as Control)?.Focus(FocusState.Programmatic);
    }

    private void ClosePowerMenu()
    {
        if (!PowerMenuOpen)
            return;
        PowerOverlay.Visibility = Visibility.Collapsed;
        PowerChoices.Children.Clear();
        UpdateHints();
        var target = _powerReturnFocus;
        _powerReturnFocus = null;
        if (target is null || !target.Focus(FocusState.Programmatic))
            RestoreFocus();
    }

    private void RunPower(PowerAction action, string progress)
    {
        ClosePowerMenu();
        Status.Text = progress;
        if (!Power.Run(action))
            Status.Text = "Windows did not allow that power action. Try it from Game Bar or the Start menu.";
    }

    private void ReturnToDesktop()
    {
        ClosePowerMenu();
        Status.Text = XboxMode.Set(false) switch
        {
            XboxMode.SetResult.Declined => "Windows did not leave Xbox mode. Use Game Bar, Task View or Win+F11 to reach the desktop.",
            XboxMode.SetResult.AppliesAfterRestart => "Windows will return to the desktop after a restart.",
            _ => Status.Text,
        };
    }

    private void PowerOverlayTapped(object sender, TappedRoutedEventArgs args) => ClosePowerMenu();
    private void PowerCardTapped(object sender, TappedRoutedEventArgs args) => args.Handled = true;
}
