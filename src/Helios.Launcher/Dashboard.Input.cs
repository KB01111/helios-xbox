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
        if (args.Key is >= VirtualKey.GamepadA and <= VirtualKey.GamepadRightThumbstickLeft)
        {
            // XInput handles this controller event to avoid double activation through XAML.
            args.Handled = true;
            return;
        }
        if (args.Key == VirtualKey.Escape)
        {
            // In Xbox mode Helios stays full screen; Escape only navigates back.
            if (_screen == Screen.Home && _window.IsFullScreen && XboxMode.IsActive() != true)
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

    private void HandleController(ControllerAction action)
    {
        if (action == ControllerAction.Back)
        {
            Back();
            return;
        }
        if (action is ControllerAction.PreviousTab or ControllerAction.NextTab)
        {
            Screen[] tabs = [Screen.Home, Screen.Library, Screen.Settings];
            int current = _screen switch { Screen.Home => 0, Screen.Settings => 2, _ => 1 };
            Navigate(tabs[(current + (action == ControllerAction.NextTab ? 1 : 2)) % tabs.Length]);
            return;
        }
        switch (action)
        {
            case ControllerAction.Menu:
                Navigate(Screen.Settings);
                return;
            case ControllerAction.Search:
                if (_screen != Screen.Library)
                    Navigate(Screen.Library);
                DispatcherQueue.TryEnqueue(ShowKeyboard);
                return;
            case ControllerAction.Favorite:
                ToggleFavoriteFromFocus();
                return;
            case ControllerAction.PreviousFilter or ControllerAction.NextFilter:
                if (_screen == Screen.Library)
                {
                    SetFilter(LibraryViews.Cycle(_filter, action == ControllerAction.NextFilter ? 1 : -1));
                    DispatcherQueue.TryEnqueue(RestoreFocus);
                }
                return;
        }
        if (action == ControllerAction.Confirm)
        {
            switch (FocusManager.GetFocusedElement(XamlRoot))
            {
                case TextBox text when text == Search:
                    ShowKeyboard();
                    break;
                case ToggleSwitch toggle when toggle.IsEnabled:
                    toggle.IsOn = !toggle.IsOn;
                    break;
                case Button button when button.IsEnabled:
                    var peer = FrameworkElementAutomationPeer.CreatePeerForElement(button);
                    if (peer?.GetPattern(PatternInterface.Invoke) is IInvokeProvider invoker)
                        invoker.Invoke();
                    break;
                default:
                    RestoreFocus();
                    break;
            }
            return;
        }

        var direction = action switch
        {
            ControllerAction.Up => FocusNavigationDirection.Up,
            ControllerAction.Down => FocusNavigationDirection.Down,
            ControllerAction.Left => FocusNavigationDirection.Left,
            _ => FocusNavigationDirection.Right,
        };
        if (FocusManager.TryMoveFocus(direction, new FindNextElementOptions { SearchRoot = Root }))
        {
            if (FocusManager.GetFocusedElement(XamlRoot) is FrameworkElement focused)
                focused.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false });
        }
        else if (FocusManager.GetFocusedElement(XamlRoot) is null)
        {
            RestoreFocus();
        }
    }

    private void ShowKeyboard()
    {
        if (SearchKeyboard.ItemsSource is null)
        {
            string[] keys = [.."ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789".Select(letter => letter.ToString()), "Space", "⌫"];
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
                button.Click += (_, _) =>
                {
                    Search.Text = key switch
                    {
                        "⌫" => Search.Text.Length != 0 ? Search.Text[..^1] : string.Empty,
                        "Space" => Search.Text + " ",
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
