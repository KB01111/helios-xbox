using Helios.Library;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Animation;

namespace Helios_Launcher;

/// <summary>
/// The startup intro. It plays once per process, doubles as the loading screen, and hands off to the dashboard as
/// soon as the animation has finished and the library is on screen. A/B, Enter, Escape or a click skips it.
/// Activations that bring an already running Helios forward (Game Bar Home) never replay it.
/// </summary>
public sealed partial class Dashboard
{
    private static readonly TimeSpan IntroTimeout = TimeSpan.FromSeconds(4.5);

    private DispatcherQueueTimer? _introTimeout;
    private Storyboard? _introIn;
    private Storyboard? _introWait;
    private bool _introPlayed;
    private bool _introLeaving;
    private bool _introDone;
    private LaunchTarget? _pendingTarget;

    private bool IntroShowing => !_introDone;

    private void PrepareIntro()
    {
        // Hide the dashboard before the first frame so it can settle in under the intro.
        if (AnimationsEnabled)
            ShellContent.Opacity = 0;
        IntroOverlay.Tapped += (_, _) => FinishIntro();
    }

    private void StartIntro()
    {
        TintIntro();
        _introTimeout = DispatcherQueue.CreateTimer();
        _introTimeout.Interval = IntroTimeout;
        _introTimeout.IsRepeating = false;
        _introTimeout.Tick += (_, _) => FinishIntro(); // Never hold the player on the intro if loading stalls.
        _introTimeout.Start();

        if (!AnimationsEnabled)
        {
            // Reduced motion: a still title card that switches instantly.
            IntroGlow.Opacity = .8;
            IntroEmblem.Opacity = 1;
            IntroWordmark.Opacity = 1;
            IntroLineScale.ScaleX = 1;
            IntroStatus.Opacity = .75;
            _introPlayed = true;
            if (_ready)
                FinishIntro();
            return;
        }

        _introIn = (Storyboard)Resources["IntroIn"];
        _introIn.Completed += (_, _) =>
        {
            _introPlayed = true;
            if (_ready)
            {
                FinishIntro();
                return;
            }
            _introWait = (Storyboard)Resources["IntroWait"];
            _introWait.Begin();
        };
        _introIn.Begin();
    }

    /// <summary>The intro takes its colors from the saved accent, so there is no color change at the hand-off.</summary>
    private void TintIntro()
    {
        if (!ConsoleTheme.TryParseColor(_theme.Accent, out var rgb))
            return;
        IntroGlowInner.Color = Microsoft.UI.ColorHelper.FromArgb(0x70, rgb.R, rgb.G, rgb.B);
        IntroGlowMid.Color = Microsoft.UI.ColorHelper.FromArgb(0x22, rgb.R, rgb.G, rgb.B);
        IntroGlowOuter.Color = Microsoft.UI.ColorHelper.FromArgb(0, rgb.R, rgb.G, rgb.B);
    }

    /// <summary>Called once the saved library is on screen.</summary>
    private void IntroLibraryReady()
    {
        if (_introPlayed)
            FinishIntro();
    }

    private void FinishIntro()
    {
        if (_introLeaving || _introDone)
            return;
        _introLeaving = true;
        _introTimeout?.Stop();
        _introWait?.Stop();
        if (!AnimationsEnabled)
        {
            CompleteIntro(null);
            return;
        }
        var outro = (Storyboard)Resources["IntroOut"];
        outro.Completed += (_, _) => CompleteIntro(outro);
        outro.Begin();
    }

    private void CompleteIntro(Storyboard? outro)
    {
        _introDone = true;
        IntroOverlay.Visibility = Visibility.Collapsed;
        // Pin the final values locally, then stop every storyboard so nothing keeps ticking behind the dashboard.
        ShellContent.Opacity = 1;
        outro?.Stop();
        _introIn?.Stop();
        _introWait?.Stop();
        if (_pendingTarget is { } target)
        {
            _pendingTarget = null;
            ShowTarget(target);
        }
        else
        {
            RestoreFocus();
        }
    }

    /// <summary>Game Bar Home/Library or helios-launcher:// asked for a page while Helios was already running.</summary>
    internal void ShowRequested(LaunchRequest launch)
    {
        if (launch.FromGamingShell && _store is null)
            _gamingShellActivatedWhileLoading = true;
        if (!launch.FromGamingShell && launch.Kind != "Protocol")
            return; // A Start menu or taskbar launch just brings Helios forward where the player left it.
        if (IntroShowing)
            _pendingTarget = launch.Target;
        else
            ShowTarget(launch.Target);
    }

    private void ShowTarget(LaunchTarget target)
    {
        ClosePowerMenu();
        KeyboardPane.Visibility = Visibility.Collapsed;
        Navigate(target == LaunchTarget.Library ? Screen.Library : Screen.Home);
    }
}
