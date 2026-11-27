using Helios.Library;
using Helios_Launcher.Windows;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Helios_Launcher;

public sealed partial class Dashboard : Page
{
    private enum Screen { Home, Library, Settings, Details, Picker }

    private readonly MainWindow _window;
    private readonly Controller _controller;
    private Screen _screen;
    private Screen _returnScreen;
    private GameEntry? _selected;
    private GameEntry? _featured;
    private IReadOnlyList<GameEntry> _games = [];
    private string? _returnFocusId;
    private bool _ready;
    private int _filter;

    internal Dashboard(MainWindow window)
    {
        _window = window;
        InitializeComponent();
        _controller = new Controller(DispatcherQueue);
        _controller.ActionReceived += HandleController;
        Loaded += LoadLibrary;
        _window.Activated += WindowActivated;
        _window.Closed += (_, _) => _controller.SetActive(false);
    }

    private void WindowActivated(object sender, WindowActivatedEventArgs args)
    {
        bool active = args.WindowActivationState != WindowActivationState.Deactivated;
        _controller.SetActive(active);
        if (active && _screen == Screen.Settings)
            _ = RefreshStartupAsync();
    }

    private void RootResized(object sender, SizeChangedEventArgs args)
    {
        bool narrow = args.NewSize.Width < 800;
        Header.Padding = narrow ? new Thickness(12, 8, 12, 8) : new Thickness(32, 15, 32, 15);
        Body.Padding = narrow ? new Thickness(14, 12, 14, 18) : new Thickness(32, 22, 32, 28);
        Footer.Padding = narrow ? new Thickness(14, 7, 14, 7) : new Thickness(32, 9, 32, 9);
        Brand.Visibility = Visible(!narrow);
        Hints.Visibility = Visible(args.NewSize.Width >= 600);
        HeroTitle.FontSize = narrow ? 33 : 44;
    }

    private void Navigate(Screen screen)
    {
        _screen = screen;
        HomePane.Visibility = Visible(screen == Screen.Home);
        LibraryPane.Visibility = Visible(screen == Screen.Library);
        SettingsPane.Visibility = Visible(screen == Screen.Settings);
        DetailsPane.Visibility = Visible(screen == Screen.Details);
        PickerPane.Visibility = Visible(screen == Screen.Picker);
        if (screen != Screen.Library)
            KeyboardPane.Visibility = Visibility.Collapsed;
        ContentScroll.ChangeView(null, 0, null, disableAnimation: true);
        var selectedBrush = (Brush)Application.Current.Resources["HeliosSelected"];
        HomeTab.Background = screen == Screen.Home ? selectedBrush : null;
        LibraryTab.Background = screen is Screen.Library or Screen.Picker ? selectedBrush : null;
        SettingsTab.Background = screen == Screen.Settings ? selectedBrush : null;
        if (screen == Screen.Settings)
            _ = RefreshStartupAsync();

        DispatcherQueue.TryEnqueue(RestoreFocus);
    }

    private void RestoreFocus()
    {
        if (_returnFocusId is not null && _screen is Screen.Home or Screen.Library)
        {
            UpdateLayout();
            bool focused = _screen == Screen.Library
                ? FocusTile(GameGrid, _returnFocusId)
                : FocusTile(Favorites, _returnFocusId) || FocusTile(Recents, _returnFocusId);
            _returnFocusId = null;
            if (focused)
                return;
        }

        Control control = _screen switch
        {
            Screen.Home => HeroAction,
            Screen.Library => _games.Count == 0 ? AddButton : LibraryTab,
            Screen.Settings => FullScreenToggle,
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
        var recent = _games.Where(game => game.LastLaunch is not null).OrderByDescending(game => game.LastLaunch).ToArray();
        var favorites = _games.Where(game => game.IsFavorite).ToArray();
        _featured = recent.FirstOrDefault(game => game.IsAvailable) ?? _games.FirstOrDefault(game => game.IsAvailable);
        HeroImage.Source = Tile.LoadArtwork(_featured?.Artwork, 1340);
        HeroFade.Visibility = Visible(HeroImage.Source is not null);
        HeroTitle.Foreground = HeroImage.Source is null ? null : new SolidColorBrush(Microsoft.UI.Colors.White);
        HeroDescription.Foreground = HeroTitle.Foreground;
        HeroSource.Foreground = HeroImage.Source is null
            ? (Brush)Application.Current.Resources["HeliosAccent"] : new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 255, 203, 135));
        HeroSource.Text = _featured is null ? "YOUR GAMES" : Tile.SourceLabel(_featured.Source).ToUpperInvariant();
        HeroTitle.Text = _featured?.Title ?? "Your games";
        HeroDescription.Text = _featured?.LastLaunch is not null ? "Jump back in." : _featured is null ? "Import installed games or add your own." : "Ready to play.";
        HeroAction.Content = _featured is null ? "Browse library" : "Play";
        Favorites.ItemsSource = favorites.Select(game => new Tile(game)).ToArray();
        Recents.ItemsSource = recent.Take(12).Select(game => new Tile(game)).ToArray();
        FavoritesRow.Visibility = Visible(favorites.Length > 0);
        RecentsRow.Visibility = Visible(recent.Length > 0);
    }

    private void ShowLibrary()
    {
        if (!_ready)
            return;
        var results = _games.Where(game => game.Title.Contains(Search.Text.Trim(), StringComparison.CurrentCultureIgnoreCase));
        results = _filter switch
        {
            1 => results.Where(game => game.Source == GameSource.Steam),
            2 => results.Where(game => game.Source == GameSource.Windows),
            3 => results.Where(game => game.Source == GameSource.Manual),
            4 => results.Where(game => game.IsFavorite),
            _ => results,
        };
        var tiles = results.Select(game => new Tile(game)).ToArray();
        GameGrid.ItemsSource = tiles;
        EmptyLibrary.Visibility = Visible(tiles.Length == 0);
        EmptyLibrary.Text = _games.Count == 0 ? "No games yet. Add a game or refresh the library." : "No games match this view.";
    }

    private void UpdateDetails()
    {
        if (_selected is not { } game)
            return;
        DetailsTitle.Text = game.Title;
        DetailsMeta.Text = new Tile(game).Subtitle;
        DetailsImage.Source = Tile.LoadArtwork(game.Artwork, 1100);
        DetailsImage.Visibility = Visible(DetailsImage.Source is not null);
        PlayButton.IsEnabled = game.IsAvailable;
        FavoriteButton.Content = game.IsFavorite ? "Remove from favorites" : "Add to favorites";
        RemoveButton.Visibility = Visible(game.Source == GameSource.Manual || game.IsPinned);
    }

    private void TileClicked(object sender, RoutedEventArgs args)
    {
        if (sender is not Button { Tag: GameEntry game })
            return;
        _selected = game;
        _returnScreen = _screen;
        UpdateDetails();
        Navigate(Screen.Details);
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

    private static Visibility Visible(bool show) => show ? Visibility.Visible : Visibility.Collapsed;

    private void HomeClicked(object sender, RoutedEventArgs args) => Navigate(Screen.Home);
    private void LibraryClicked(object sender, RoutedEventArgs args) => Navigate(Screen.Library);
    private void SettingsClicked(object sender, RoutedEventArgs args) => Navigate(Screen.Settings);
    private void BackClicked(object sender, RoutedEventArgs args) => Back();
    private void CloseClicked(object sender, RoutedEventArgs args) => _window.Close();
    private void SearchChanged(object sender, TextChangedEventArgs args) => ShowLibrary();
    private void FilterClicked(object sender, RoutedEventArgs args)
    {
        string[] filters = ["All games", "Steam", "Xbox & Store", "Manual", "Favorites"];
        _filter = (_filter + 1) % filters.Length;
        Filter.Content = filters[_filter];
        ShowLibrary();
    }
}
