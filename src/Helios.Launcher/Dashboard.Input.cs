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
            if (_screen == Screen.Home && _window.IsFullScreen)
                ApplyFullScreen(false);
            else
                Back();
            args.Handled = true;
        }
        else if (args.Key == VirtualKey.F11)
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
            SearchKeyboard.ItemsSource = keys.Select(key =>
            {
                var button = new Button { Content = key, MinHeight = 38, MinWidth = 42, HorizontalAlignment = HorizontalAlignment.Stretch };
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

    private void SearchKeysClicked(object sender, RoutedEventArgs args) => ShowKeyboard();
    private void DoneTyping(object sender, RoutedEventArgs args) => Back();
}
