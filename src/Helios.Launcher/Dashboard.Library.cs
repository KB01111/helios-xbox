using System.ComponentModel;
using System.Runtime.InteropServices;
using Helios.Library;
using Helios_Launcher.Windows;
using Microsoft.Data.Sqlite;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Win32;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Helios_Launcher;

public sealed partial class Dashboard
{
    private LibraryStore? _store;
    private bool _refreshing;
    private bool _picking;
    private bool _launching;
    private long _lastLaunchAttempt = long.MinValue;
    private string ArtworkDirectory => Path.Combine(_window.DataDirectory, "artwork");

    private async void LoadLibrary(object sender, RoutedEventArgs args)
    {
        Loaded -= LoadLibrary;
        Navigate(_launch.Target == LaunchTarget.Library ? Screen.Library : Screen.Home);
        StartIntro();
        try
        {
            var (store, quarantined) = await Task.Run(() => LibraryStore.OpenOrRecover(Path.Combine(_window.DataDirectory, "library.db")));
            _store = store;
            _games = await Task.Run(_store.ReadGames);
            _ready = true;
            LoadTheme();
            UpdateLibraryViews();
            DesktopFullScreenPreference = _store.GetSetting("fullScreen") != "false";
            if (DesktopFullScreenPreference && !_window.IsFullScreen)
                _window.SetFullScreen(true);
            IntroLibraryReady();
            if (quarantined is not null)
            {
                RecoveryText.Text = $"The game library was damaged and has been rebuilt. The old copy is {Path.GetFileName(quarantined)} in the Helios data folder.";
                RecoveryText.Visibility = Visibility.Visible;
            }
            await RefreshLibraryAsync();
            if (quarantined is not null)
                Status.Text = "The library was damaged and has been rebuilt from your installed games. Favorites need to be set again.";
        }
        catch (Exception error) when (IsLocalError(error))
        {
            Status.Text = $"Could not open the game library. {error.Message}";
            _ready = true; // Show the dashboard (Settings and power still work) instead of holding on the intro.
            IntroLibraryReady();
        }
    }

    private async Task RefreshLibraryAsync()
    {
        if (_store is not { } store || _refreshing)
            return;
        _refreshing = true;
        RefreshButton.IsEnabled = false;
        Status.Text = "Scanning installed games…";
        try
        {
            var pinned = _games.Where(game => game.IsPinned).Select(game => game.Target).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var result = await Task.Run(async () =>
            {
                using var user = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
                using var machine = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam");
                var steamRoot = user?.GetValue("SteamPath") as string ?? machine?.GetValue("InstallPath") as string;
                var steam = SteamLibrary.Scan(steamRoot);
                var windows = await WindowsGameLibrary.ScanAsync(ArtworkDirectory, pinned);
                store.ApplyScan(GameSource.Steam, steam.Games, steam.Complete);
                store.ApplyScan(GameSource.Windows, windows.Games, windows.Complete);
                return (Games: store.ReadGames(), Complete: (steamRoot is null || steam.Complete) && windows.Complete);
            });
            _games = result.Games;
            UpdateLibraryViews();
            int available = _games.Count(game => game.IsAvailable);
            Status.Text = result.Complete ? $"{available} available" : $"{available} available · Some sources could not finish scanning";
        }
        catch (Exception error) when (IsLocalError(error))
        {
            Status.Text = $"Refresh failed. Your saved library is still available. {error.Message}";
        }
        finally
        {
            _refreshing = false;
            RefreshButton.IsEnabled = true;
        }
    }

    private void ReloadGames()
    {
        if (_store is null)
            return;
        _games = _store.ReadGames();
        UpdateLibraryViews();
    }

    private void UpdateLibraryViews()
    {
        var focusedId = XamlRoot is not null && Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(XamlRoot)
            is Button { Tag: GameEntry focused } ? focused.Id : null;
        ShowHome();
        ShowLibrary();
        if (_selected is not null)
        {
            _selected = _games.FirstOrDefault(game => game.Id == _selected.Id) ?? _selected;
            UpdateDetails();
        }
        if (focusedId is not null && _screen is Screen.Home or Screen.Library)
        {
            _returnFocusId = focusedId;
            DispatcherQueue.TryEnqueue(RestoreFocus);
        }
    }

    private async Task LaunchAsync(GameEntry? game)
    {
        if (game is null || _launching)
            return;
        long now = Environment.TickCount64;
        if (_lastLaunchAttempt != long.MinValue && now - _lastLaunchAttempt < 1500)
            return;
        _lastLaunchAttempt = now;
        _launching = true;
        Status.Text = $"Opening {game.Title}…";
        try
        {
            await Task.Run(() => GameLaunch.StartAsync(game));
        }
        catch (Exception error) when (IsLocalError(error))
        {
            Status.Text = $"Could not start {game.Title}. {error.Message}";
            _lastLaunchAttempt = long.MinValue;
            return;
        }
        finally
        {
            _launching = false;
        }
        try
        {
            _store?.RecordLaunch(game.Id, DateTimeOffset.UtcNow);
            ReloadGames();
            Status.Text = $"Launch requested for {game.Title}";
        }
        catch (SqliteException)
        {
            Status.Text = $"Launch requested for {game.Title}. Recent history could not be saved.";
        }
    }

    private async void AddFileClicked(object sender, RoutedEventArgs args)
    {
        if (_store is null || _picking)
            return;
        _picking = true;
        try
        {
            var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
            picker.FileTypeFilter.Add(".exe");
            picker.FileTypeFilter.Add(".lnk");
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(_window));
            var file = await picker.PickSingleFileAsync();
            if (file is null)
                return;
            string path = Path.GetFullPath(file.Path);
            if (!GameEntry.IsValidTarget(GameSource.Manual, path))
                throw new InvalidOperationException("Choose a local executable or Windows shortcut.");
            string id = $"manual:{path.ToUpperInvariant()}";
            _store.ApplyScan(GameSource.Manual, [new GameEntry(id, Path.GetFileNameWithoutExtension(path), GameSource.Manual, path)], false);
            ReloadGames();
            ShowAllGames();
            _returnFocusId = id;
            Navigate(Screen.Library);
            Status.Text = $"{Path.GetFileNameWithoutExtension(path)} added";
        }
        catch (Exception error) when (IsLocalError(error))
        {
            Status.Text = $"Could not add the file. {error.Message}";
        }
        finally
        {
            _picking = false;
        }
    }

    private async void AddWindowsClicked(object sender, RoutedEventArgs args)
    {
        if (_store is null || _picking)
            return;
        _picking = true;
        WindowsPicker.ItemsSource = null;
        Navigate(Screen.Picker);
        Status.Text = "Finding installed Windows apps…";
        try
        {
            var scan = await Task.Run(() => WindowsGameLibrary.ScanAsync(ArtworkDirectory, allApps: true));
            if (_screen != Screen.Picker)
                return;
            WindowsPicker.ItemsSource = scan.Games.Select(game => new Tile(game, GridTileStyle)).ToArray();
            Status.Text = scan.Games.Count == 0 ? "No eligible apps found" : "Choose the installed title to add";
        }
        catch (Exception error) when (IsLocalError(error))
        {
            Status.Text = $"Could not list installed apps. {error.Message}";
        }
        finally
        {
            _picking = false;
        }
    }

    private void WindowsAppClicked(object sender, RoutedEventArgs args)
    {
        if (_store is null || sender is not Button { Tag: GameEntry game })
            return;
        try
        {
            _store.ApplyScan(GameSource.Windows, [game], false);
            _store.SetPinned(game.Id, true);
            ReloadGames();
            ShowAllGames();
            _returnFocusId = game.Id;
            Navigate(Screen.Library);
            Status.Text = $"{game.Title} added";
        }
        catch (SqliteException error)
        {
            Status.Text = $"Could not add {game.Title}. {error.Message}";
        }
    }

    private void ShowAllGames()
    {
        Search.Text = string.Empty;
        SetFilter(LibraryFilter.All);
    }

    private void FavoriteClicked(object sender, RoutedEventArgs args)
    {
        if (_selected is { } game)
            ToggleFavorite(game);
    }

    private void ToggleFavorite(GameEntry game)
    {
        if (_store is null)
            return;
        try
        {
            _store.SetFavorite(game.Id, !game.IsFavorite);
            ReloadGames();
            Status.Text = game.IsFavorite ? $"{game.Title} removed from favorites" : $"{game.Title} added to favorites";
        }
        catch (SqliteException error)
        {
            Status.Text = $"Could not save favorites. {error.Message}";
        }
    }

    private void RemoveClicked(object sender, RoutedEventArgs args)
    {
        if (_selected is not { } game || _store is null || (!game.IsPinned && game.Source != GameSource.Manual))
            return;
        try
        {
            _store.SetPinned(game.Id, false);
            _store.RemoveGame(game.Id);
            _selected = null;
            ReloadGames();
            Navigate(_returnScreen);
            Status.Text = $"{game.Title} removed from the library";
        }
        catch (SqliteException error)
        {
            Status.Text = $"Could not remove {game.Title}. {error.Message}";
        }
    }

    private static bool IsLocalError(Exception error) =>
        error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException
            or Win32Exception or COMException or SqliteException or System.Security.SecurityException;

    private async void RefreshClicked(object sender, RoutedEventArgs args) => await RefreshLibraryAsync();
    private async void PlayClicked(object sender, RoutedEventArgs args) => await LaunchAsync(_selected);
    private async void HeroClicked(object sender, RoutedEventArgs args)
    {
        if (_featured is { } game)
            await LaunchAsync(game);
        else
            Navigate(Screen.Library);
    }
}
