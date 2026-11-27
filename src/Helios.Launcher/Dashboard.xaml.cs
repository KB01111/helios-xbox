using System.Numerics;
using Helios.Library;
using Helios_Launcher.Windows;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Helios_Launcher;

public sealed partial class Dashboard : Page
{
    private enum Screen { Home, Library, Settings, Details, Picker }

    private static readonly Vector3 FocusScale = new(1.06f, 1.06f, 1);
    private const int BackdropDecodeWidth = 1600;

    private readonly MainWindow _window;
    private readonly Controller _controller;
    private readonly DispatcherQueueTimer _clock;
    private readonly DispatcherQueueTimer _backdropDelay;
    private Screen _screen;
    private Screen _returnScreen;
    private GameEntry? _selected;
    private GameEntry? _featured;
    private GameEntry? _pendingBackdrop;
    private string? _backdropPath;
    private bool _backdropOnA;
    private IReadOnlyList<GameEntry> _games = [];
    private string? _returnFocusId;
    private bool _ready;
    private bool _compact;
    private LibraryFilter _filter;
    private readonly Dictionary<LibraryFilter, Button> _chips = [];

    internal Dashboard(MainWindow window)
    {
        _window = window;
        InitializeComponent();
        _controller = new Controller(DispatcherQueue);
        _controller.ActionReceived += HandleController;
        _clock = DispatcherQueue.CreateTimer();
        _clock.Interval = TimeSpan.FromSeconds(15);
        _clock.Tick += (_, _) => UpdateClock();
        _backdropDelay = DispatcherQueue.CreateTimer();
        _backdropDelay.Interval = TimeSpan.FromMilliseconds(90);
        _backdropDelay.IsRepeating = false;
        _backdropDelay.Tick += (_, _) => ApplyBackdrop(_pendingBackdrop);
        BuildFilterChips();
        Loaded += LoadLibrary;
        _window.Activated += WindowActivated;
        _window.XboxModeChanged += XboxModeChanged;
        _window.Closed += (_, _) =>
        {
            _controller.SetActive(false);
            _clock.Stop();
        };
        UpdateClock();
        _clock.Start();
        UpdateXboxChrome();
    }

    private void WindowActivated(object sender, WindowActivatedEventArgs args)
    {
        bool active = args.WindowActivationState != WindowActivationState.Deactivated;
        _controller.SetActive(active);
        if (active)
        {
            UpdateClock();
            UpdateXboxChrome();
            if (_screen == Screen.Settings)
                _ = RefreshStartupAsync();
        }
    }

    private void XboxModeChanged()
    {
        UpdateXboxChrome();
        if (XboxMode.IsActive() == true)
            _window.SetFullScreen(true);
        else
            _window.SetFullScreen(_store?.GetSetting("fullScreen") == "true");
        if (_screen == Screen.Settings)
            _ = RefreshStartupAsync();
    }

    private void UpdateXboxChrome()
    {
        bool active = XboxMode.IsActive() == true;
        XboxBadge.Visibility = Visible(active);
        // As the Xbox mode home, closing Helios would leave an empty session, so offer the desktop instead.
        ExitGlyph.Glyph = active ? "" : "";
        ExitLabel.Visibility = Visible(active);
        AutomationPropertiesHelper.SetName(ExitButton, active ? "Return to the Windows desktop" : "Exit Helios");
    }

    private void UpdateClock() => Clock.Text = DateTime.Now.ToString("t");

    private void RootResized(object sender, SizeChangedEventArgs args)
    {
        bool narrow = args.NewSize.Width < 900;
        double gutter = narrow ? 16 : 40;
        Header.Padding = new Thickness(gutter, narrow ? 10 : 18, gutter, 6);
        Body.Padding = new Thickness(gutter, 8, gutter, 24);
        Footer.Padding = new Thickness(gutter, 8, gutter, narrow ? 10 : 16);
        Brand.Visibility = Visible(!narrow);
        Clock.Visibility = Visible(args.NewSize.Width >= 700);
        Hints.Visibility = Visible(args.NewSize.Width >= 760);
        HeroTitle.FontSize = narrow ? 38 : 56;
        HeroTitle.LineHeight = narrow ? 44 : 62;
        // Keep the hero and the first shelf on screen together, from handhelds (800p) to TVs.
        // Header, footer and one shelf take about 390 px; the hero gets the rest.
        HeroArea.Height = Math.Clamp(args.NewSize.Height - 390, 180, 460);
        // On short screens (handhelds) the backdrop already shows the art, so keep Play above the fold.
        _compact = args.NewSize.Height < 820;
        DetailsArtFrame.Visibility = Visible(!_compact && DetailsImage.Source is not null);
    }

    private void Navigate(Screen screen)
    {
        bool changed = _screen != screen;
        _screen = screen;
        HomePane.Visibility = Visible(screen == Screen.Home);
        LibraryPane.Visibility = Visible(screen == Screen.Library);
        SettingsPane.Visibility = Visible(screen == Screen.Settings);
        DetailsPane.Visibility = Visible(screen == Screen.Details);
        PickerPane.Visibility = Visible(screen == Screen.Picker);
        if (screen != Screen.Library)
            KeyboardPane.Visibility = Visibility.Collapsed;
        ContentScroll.ChangeView(null, 0, null, disableAnimation: true);
        SetTab(HomeTab, screen == Screen.Home);
        SetTab(LibraryTab, screen is Screen.Library or Screen.Picker);
        SetTab(SettingsTab, screen == Screen.Settings);
        if (changed)
            AnimateIn(screen switch
            {
                Screen.Home => HomePane,
                Screen.Library => LibraryPane,
                Screen.Settings => SettingsPane,
                Screen.Details => DetailsPane,
                _ => PickerPane,
            });
        if (screen == Screen.Settings)
            _ = RefreshStartupAsync();
        if (screen == Screen.Home)
            QueueBackdrop(_featured);
        else if (screen == Screen.Details)
            QueueBackdrop(_selected);
        UpdateHints();
        DispatcherQueue.TryEnqueue(RestoreFocus);
    }

    private void AnimateIn(UIElement pane)
    {
        pane.Opacity = 0;
        pane.Translation = new Vector3(0, 16, 0);
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            pane.Opacity = 1;
            pane.Translation = Vector3.Zero;
        });
    }

    private static void SetTab(Button tab, bool selected)
    {
        tab.Foreground = selected
            ? ThemeBrush("TextFillColorPrimaryBrush")
            : ThemeBrush("HeliosMuted");
        tab.Background = selected ? ThemeBrush("HeliosChip") : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
    }

    // The app is pinned to the dark theme (App.xaml), so app-level lookups return the active theme brush.
    private static Brush ThemeBrush(string key) => (Brush)Application.Current.Resources[key];

    private void RestoreFocus()
    {
        if (_returnFocusId is not null && _screen is Screen.Home or Screen.Library)
        {
            UpdateLayout();
            bool focused = _screen == Screen.Library
                ? FocusTile(GameGrid, _returnFocusId)
                : FocusTile(Shelf, _returnFocusId) || FocusTile(Favorites, _returnFocusId);
            _returnFocusId = null;
            if (focused)
                return;
        }

        Control control = _screen switch
        {
            Screen.Home => _games.Count > 0 && Shelf.TryGetElement(0) is Control first ? first : HeroAction,
            Screen.Library => _games.Count == 0 ? AddButton : GameGrid.TryGetElement(0) as Control ?? _chips[_filter],
            Screen.Settings => ChooseHomeButton,
            Screen.Details => PlayButton.IsEnabled ? PlayButton : BackButton,
            Screen.Picker => WindowsPicker,
            _ => HomeTab,
        };
        control.Focus(FocusState.Programmatic);
    }

    private static bool FocusTile(ItemsRepeater repeater, string id)
    {
        if (repeater.ItemsSource is not Tile[] tiles)
            return false;
        int index = Array.FindIndex(tiles, tile => tile.Game.Id == id);
        if (index < 0)
            return false;
        if (repeater.GetOrCreateElement(index) is not Control control)
            return false;
        control.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false });
        return control.Focus(FocusState.Programmatic);
    }

    private void ShowHome()
    {
        var shelf = LibraryViews.HomeShelf(_games);
        var favorites = LibraryViews.Favorites(_games);
        bool anyRecent = shelf.Any(game => game.LastLaunch is not null);
        if (_featured is null || !_games.Any(game => game.Id == _featured.Id))
            _featured = shelf.FirstOrDefault();
        else
            _featured = _games.First(game => game.Id == _featured.Id);
        ShelfTitle.Text = anyRecent ? "Jump back in" : "Your games";
        Shelf.ItemsSource = shelf.Select(game => new Tile(game)).ToArray();
        Favorites.ItemsSource = favorites.Select(game => new Tile(game)).ToArray();
        ShelfRow.Visibility = Visible(shelf.Count > 0);
        FavoritesRow.Visibility = Visible(favorites.Count > 0);
        UpdateHero(_featured);
    }

    private void UpdateHero(GameEntry? game)
    {
        HeroSource.Text = game is null ? "HELIOS" : Tile.SourceLabel(game.Source).ToUpperInvariant();
        HeroTitle.Text = game?.Title ?? "Your games";
        HeroDescription.Text = game is null
            ? "Import your installed games or add your own."
            : !game.IsAvailable ? "Unavailable. Refresh the library or reinstall it."
            : game.IsFavorite ? $"{Tile.LastPlayed(game.LastLaunch)} · Favorite" : Tile.LastPlayed(game.LastLaunch);
        HeroActionText.Text = game is null ? "Browse library" : "Play";
        HeroActionGlyph.Glyph = game is null ? "" : "";
        HeroAction.IsEnabled = game is null || game.IsAvailable;
        HeroDetails.Visibility = Visible(game is not null);
        if (_screen == Screen.Home)
            QueueBackdrop(game);
    }

    private void QueueBackdrop(GameEntry? game)
    {
        _pendingBackdrop = game;
        _backdropDelay.Stop();
        _backdropDelay.Start();
    }

    // Store logos are icons, not scenery, so only real artwork becomes the ambient backdrop.
    private void ApplyBackdrop(GameEntry? game)
    {
        string? path = game is { Source: not GameSource.Windows } ? game.Artwork : null;
        if (path == _backdropPath)
            return;
        _backdropPath = path;
        BitmapImage? image = Tile.LoadArtwork(path, BackdropDecodeWidth);
        var incoming = _backdropOnA ? BackdropB : BackdropA;
        var outgoing = _backdropOnA ? BackdropA : BackdropB;
        _backdropOnA = !_backdropOnA;
        incoming.Source = image;
        incoming.Opacity = image is null ? 0 : .55;
        outgoing.Opacity = 0;
    }

    private void ShowLibrary()
    {
        if (!_ready)
            return;
        var tiles = LibraryViews.Filter(_games, Search.Text, _filter).Select(game => new Tile(game)).ToArray();
        GameGrid.ItemsSource = tiles;
        LibraryCount.Text = tiles.Length == _games.Count ? $"{_games.Count} games" : $"{tiles.Length} of {_games.Count}";
        EmptyLibrary.Visibility = Visible(tiles.Length == 0);
        EmptyLibrary.Text = _games.Count == 0 ? "No games yet. Add a game or refresh the library." : "No games match this view.";
    }

    private void BuildFilterChips()
    {
        foreach (var filter in LibraryViews.Filters)
        {
            var chip = new Button
            {
                Content = LibraryViews.Label(filter),
                Style = (Style)Application.Current.Resources["HeliosPill"],
                Tag = filter,
            };
            chip.Click += (_, _) => SetFilter(filter);
            _chips[filter] = chip;
            FilterChips.Children.Add(chip);
        }
        RenderChips();
    }

    private void SetFilter(LibraryFilter filter)
    {
        _filter = filter;
        RenderChips();
        ShowLibrary();
    }

    private void RenderChips()
    {
        foreach (var (filter, chip) in _chips)
        {
            bool selected = filter == _filter;
            chip.Background = selected ? ThemeBrush("HeliosAccent") : ThemeBrush("HeliosChip");
            chip.Foreground = selected ? ThemeBrush("HeliosOnAccent") : ThemeBrush("TextFillColorPrimaryBrush");
            AutomationPropertiesHelper.SetName(chip, selected ? $"{LibraryViews.Label(filter)}, selected filter" : $"{LibraryViews.Label(filter)} filter");
        }
    }

    private void UpdateDetails()
    {
        if (_selected is not { } game)
            return;
        DetailsSource.Text = Tile.SourceLabel(game.Source).ToUpperInvariant();
        DetailsTitle.Text = game.Title;
        DetailsMeta.Text = game.IsAvailable ? Tile.LastPlayed(game.LastLaunch) : "Unavailable. Refresh the library or add it again.";
        DetailsTarget.Text = game.Source == GameSource.Steam ? $"Steam app {game.Target}" : game.Target;
        DetailsImage.Source = Tile.LoadArtwork(game.Artwork, 1040);
        DetailsImage.Stretch = game.Source == GameSource.Windows ? Stretch.Uniform : Stretch.UniformToFill;
        DetailsArtFrame.Visibility = Visible(!_compact && DetailsImage.Source is not null);
        PlayButton.IsEnabled = game.IsAvailable;
        FavoriteGlyph.Glyph = game.IsFavorite ? "" : "";
        FavoriteText.Text = game.IsFavorite ? "Favorited" : "Favorite";
        RemoveButton.Visibility = Visible(game.Source == GameSource.Manual || game.IsPinned);
    }

    private void TileClicked(object sender, RoutedEventArgs args)
    {
        if (sender is not Button { Tag: GameEntry game })
            return;
        OpenDetails(game);
    }

    private void OpenDetails(GameEntry game)
    {
        _selected = game;
        _returnScreen = _screen is Screen.Home or Screen.Library ? _screen : Screen.Home;
        UpdateDetails();
        Navigate(Screen.Details);
    }

    private void TileFocused(object sender, RoutedEventArgs args)
    {
        if (sender is not Button { Tag: GameEntry game } tile)
            return;
        tile.Scale = FocusScale;
        tile.BorderThickness = new Thickness(3);
        if (_screen == Screen.Home)
        {
            _featured = game;
            UpdateHero(game);
        }
        else if (_screen == Screen.Library)
        {
            QueueBackdrop(game);
        }
    }

    private void TileUnfocused(object sender, RoutedEventArgs args)
    {
        if (sender is not Button tile)
            return;
        tile.Scale = Vector3.One;
        tile.BorderThickness = new Thickness(0);
    }

    private void Back()
    {
        if (KeyboardPane.Visibility == Visibility.Visible)
        {
            KeyboardPane.Visibility = Visibility.Collapsed;
            SearchKeys.Focus(FocusState.Programmatic);
        }
        else if (_screen == Screen.Details)
        {
            _returnFocusId = _selected?.Id;
            Navigate(_returnScreen);
        }
        else if (_screen is Screen.Library or Screen.Settings)
        {
            Navigate(Screen.Home);
        }
        else if (_screen == Screen.Picker)
        {
            Navigate(Screen.Library);
        }
    }

    private void UpdateHints()
    {
        (string Button, string Label)[] hints = _screen switch
        {
            Screen.Home => [("A", "Select"), ("Y", "Favorite"), ("LB RB", "Tabs")],
            Screen.Library => [("A", "Select"), ("B", "Back"), ("X", "Search"), ("Y", "Favorite"), ("LT RT", "Filter")],
            Screen.Details => [("A", "Select"), ("B", "Back"), ("Y", "Favorite")],
            _ => [("A", "Select"), ("B", "Back"), ("LB RB", "Tabs")],
        };
        Hints.Children.Clear();
        foreach (var (button, label) in hints)
            Hints.Children.Add(Hint(button, label));
    }

    private static StackPanel Hint(string button, string label)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
        foreach (string name in button.Split(' '))
        {
            var color = name switch
            {
                "A" => Microsoft.UI.ColorHelper.FromArgb(255, 108, 196, 74),
                "B" => Microsoft.UI.ColorHelper.FromArgb(255, 232, 82, 82),
                "X" => Microsoft.UI.ColorHelper.FromArgb(255, 76, 146, 235),
                "Y" => Microsoft.UI.ColorHelper.FromArgb(255, 240, 192, 48),
                _ => Microsoft.UI.ColorHelper.FromArgb(255, 150, 156, 170),
            };
            bool face = name.Length == 1;
            panel.Children.Add(new Border
            {
                MinWidth = face ? 24 : 30,
                Height = 24,
                Padding = new Thickness(face ? 0 : 6, 0, face ? 0 : 6, 0),
                CornerRadius = new CornerRadius(12),
                BorderThickness = new Thickness(1.5),
                BorderBrush = new SolidColorBrush(color),
                Child = new TextBlock
                {
                    Text = name,
                    FontSize = face ? 12 : 10,
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    Foreground = new SolidColorBrush(color),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            });
        }
        panel.Children.Add(new TextBlock { Text = label, FontSize = 13, VerticalAlignment = VerticalAlignment.Center });
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAccessibilityView(panel, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        return panel;
    }

    private static Visibility Visible(bool show) => show ? Visibility.Visible : Visibility.Collapsed;

    private void HomeClicked(object sender, RoutedEventArgs args) => Navigate(Screen.Home);
    private void LibraryClicked(object sender, RoutedEventArgs args) => Navigate(Screen.Library);
    private void SettingsClicked(object sender, RoutedEventArgs args) => Navigate(Screen.Settings);
    private void BackClicked(object sender, RoutedEventArgs args) => Back();
    private void CloseClicked(object sender, RoutedEventArgs args) => _window.Close();
    private void SearchChanged(object sender, TextChangedEventArgs args) => ShowLibrary();
    private void HeroDetailsClicked(object sender, RoutedEventArgs args)
    {
        if (_featured is { } game)
            OpenDetails(game);
    }

    private void ExitClicked(object sender, RoutedEventArgs args)
    {
        if (XboxMode.IsActive() != true)
        {
            _window.Close();
            return;
        }
        if (!XboxMode.TrySet(false))
            Status.Text = "Windows did not leave Xbox mode. Use Game Bar or Task View to reach the desktop.";
    }
}

internal static class AutomationPropertiesHelper
{
    internal static void SetName(DependencyObject element, string name) =>
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(element, name);
}
