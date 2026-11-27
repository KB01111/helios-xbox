using Helios.Library;
using Helios_Launcher.Windows;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Helios_Launcher;

public sealed partial class Dashboard
{
    private void RootKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (IntroShowing)
        {
            // Nothing behind the intro should move; Enter, Space or Escape skips straight to the dashboard.
            if (args.Key is VirtualKey.Enter or VirtualKey.Space or VirtualKey.Escape)
                FinishIntro();
            args.Handled = args.Key is not VirtualKey.F11;
            if (args.Handled)
                return;
        }
        if (args.Key is >= VirtualKey.GamepadA and <= VirtualKey.GamepadRightThumbstickLeft)
        {
            // XInput handles this controller event to avoid double activation through XAML.
            args.Handled = true;
            return;
        }
        if (PowerMenuOpen && args.Key is VirtualKey.Up or VirtualKey.Down or VirtualKey.Left or VirtualKey.Right)
        {
            // Keep arrow keys inside the modal power menu.
            MoveFocus(args.Key switch
            {
                VirtualKey.Up => FocusNavigationDirection.Up,
                VirtualKey.Down => FocusNavigationDirection.Down,
                VirtualKey.Left => FocusNavigationDirection.Left,
                _ => FocusNavigationDirection.Right,
            });
            args.Handled = true;
            return;
        }
        if (args.Key == VirtualKey.Escape)
        {
            // In Xbox mode Helios stays full screen; Escape only navigates back.
            if (!PowerMenuOpen && _screen == Screen.Home && _window.IsFullScreen && XboxMode.IsActive() != true)
                ApplyFullScreen(false);
            else
                Back();
            args.Handled = true;
        }
        else if (args.Key == VirtualKey.F11 && XboxMode.IsActive() != true)
        {
            ApplyFullScreen(!_window.IsFullScreen);
            args.Handled = true;
        }
    }

    // One small handler per controller action keeps each path flat and easy to test by hand.
    private void HandleController(ControllerAction action)
    {
        if (IntroShowing)
        {
            if (action is ControllerAction.Confirm or ControllerAction.Back or ControllerAction.Menu)
                FinishIntro();
            return;
        }
        // The power menu is modal: only movement, select and back reach it.
        if (PowerMenuOpen && action is not (ControllerAction.Up or ControllerAction.Down or ControllerAction.Left
            or ControllerAction.Right or ControllerAction.Confirm or ControllerAction.Back or ControllerAction.View))
            return;
        Action handler = action switch
        {
            ControllerAction.Back => Back,
            ControllerAction.PreviousTab => () => CycleTab(-1),
            ControllerAction.NextTab => () => CycleTab(1),
            ControllerAction.Menu => () => Navigate(Screen.Settings),
            ControllerAction.View => () =>
            {
                if (PowerMenuOpen)
                    ClosePowerMenu();
                else
                    OpenPowerMenu(fromClose: false);
            },
            ControllerAction.Search => OpenSearch,
            ControllerAction.Favorite => ToggleFavoriteFromFocus,
            ControllerAction.PreviousFilter => () => CycleFilter(-1),
            ControllerAction.NextFilter => () => CycleFilter(1),
            ControllerAction.Confirm => ConfirmFocused,
            ControllerAction.Up => () => MoveFocus(FocusNavigationDirection.Up),
            ControllerAction.Down => () => MoveFocus(FocusNavigationDirection.Down),
            ControllerAction.Left => () => MoveFocus(FocusNavigationDirection.Left),
            _ => () => MoveFocus(FocusNavigationDirection.Right),
        };
        handler();
    }

    private void CycleTab(int step)
    {
        Screen[] tabs = [Screen.Home, Screen.Library, Screen.Settings];
        int current = _screen switch { Screen.Home => 0, Screen.Settings => 2, _ => 1 };
        Navigate(tabs[(current + step + tabs.Length) % tabs.Length]);
    }

    private void OpenSearch()
    {
        if (_screen != Screen.Library)
            Navigate(Screen.Library);
        DispatcherQueue.TryEnqueue(ShowKeyboard);
    }

    private void CycleFilter(int step)
    {
        if (_screen != Screen.Library)
            return;
        SetFilter(LibraryViews.Cycle(_filter, step));
        DispatcherQueue.TryEnqueue(RestoreFocus);
    }

    private void ConfirmFocused()
    {
        switch (FocusManager.GetFocusedElement(XamlRoot))
        {
            case TextBox text when text == Search:
                ShowKeyboard();
                break;
            case ToggleSwitch { IsEnabled: true } toggle:
                toggle.IsOn = !toggle.IsOn;
                break;
            case Button { IsEnabled: true } button:
                Invoke(button);
                break;
            default:
                RestoreFocus();
                break;
        }
    }

    private static void Invoke(Button button)
    {
        var peer = FrameworkElementAutomationPeer.CreatePeerForElement(button);
        if (peer?.GetPattern(PatternInterface.Invoke) is IInvokeProvider invoker)
            invoker.Invoke();
    }

    private void MoveFocus(FocusNavigationDirection direction)
    {
        var root = PowerMenuOpen ? (DependencyObject)PowerOverlay : Root;
        if (!FocusManager.TryMoveFocus(direction, new FindNextElementOptions { SearchRoot = root }))
        {
            if (FocusManager.GetFocusedElement(XamlRoot) is null)
                RestoreFocus();
            return;
        }
        if (FocusManager.GetFocusedElement(XamlRoot) is FrameworkElement focused)
            focused.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false });
    }

    private void ShowKeyboard()
    {
        if (SearchKeyboard.ItemsSource is null)
        {
            string[] keys = [.."ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789".Select(letter => letter.ToString()), "Space", "⌫", "Clear"];
            var style = (Style)Application.Current.Resources["HeliosPill"];
            SearchKeyboard.ItemsSource = keys.Select(key =>
            {
                var button = new Button
                {
                    Content = key, Style = style, MinHeight = 46, Padding = new Thickness(0), CornerRadius = new CornerRadius(12),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
                };
                if (key == "⌫")
                    Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, "Delete");
                else if (key == "Clear")
                    Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, "Clear search");
                button.Click += (_, _) =>
                {
                    Search.Text = key switch
                    {
                        "⌫" => Search.Text.Length != 0 ? Search.Text[..^1] : string.Empty,
                        "Space" => Search.Text + " ",
                        "Clear" => string.Empty,
                        _ => Search.Text + key,
                    };
                };
                return button;
            }).ToArray();
        }
        KeyboardPane.Visibility = Visibility.Visible;
        DispatcherQueue.TryEnqueue(() =>
        {
            UpdateLayout();
            (SearchKeyboard.GetOrCreateElement(0) as Button)?.Focus(FocusState.Programmatic);
        });
    }

    private void ToggleFavoriteFromFocus()
    {
        GameEntry? game = FocusManager.GetFocusedElement(XamlRoot) is Button { Tag: GameEntry focused } ? focused
            : _screen == Screen.Details ? _selected : null;
        if (game is not null)
            ToggleFavorite(game);
    }

    private void SearchKeysClicked(object sender, RoutedEventArgs args) => ShowKeyboard();
    private void DoneTyping(object sender, RoutedEventArgs args) => Back();
}
