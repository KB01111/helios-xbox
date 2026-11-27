using Helios.Library;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Helios_Launcher;

/// <summary>Console themes: Home layouts, tile shapes, corners, accent and background, applied live.</summary>
public sealed partial class Dashboard
{
    private const string ThemeSetting = "theme";
    private const long MaxPictureBytes = 25 * 1024 * 1024;

    private ConsoleTheme _theme = ConsoleTheme.Default;
    private double _backdropOpacity = .55;
    private global::Windows.Foundation.Size _size = new(1440, 900);
    private readonly List<Action> _appearanceRenderers = [];
    private bool _renderingAppearance;

    private TileStyle HomeTileStyle => TileStyle.For(_theme, _theme.Layout);
    private TileStyle GridTileStyle => TileStyle.For(_theme, HomeLayout.Grid);

    private void LoadTheme()
    {
        var saved = ConsoleTheme.FromJson(_store?.GetSetting(ThemeSetting));
        ApplyTheme(saved ?? ConsoleTheme.Default, persist: false);
    }

    private void ApplyTheme(ConsoleTheme theme, bool persist)
    {
        _theme = theme;
        ApplyAccent(theme.Accent);
        ApplyLayout();
        ApplyLayoutMetrics();
        _backdropPath = "\0"; // Force the backdrop to re-evaluate for the new mode.
        ApplyBackdrop(_screen == Screen.Details ? _selected : _featured);
        if (_ready)
        {
            ShowHome();
            ShowLibrary();
        }
        RenderAppearance();
        if (!persist)
            return;
        try
        {
            _store?.SetSetting(ThemeSetting, theme.ToJson());
        }
        catch (Microsoft.Data.Sqlite.SqliteException error)
        {
            Status.Text = $"The theme could not be saved. {error.Message}";
        }
    }

    private static void ApplyAccent(string accent)
    {
        if (!ConsoleTheme.TryParseColor(accent, out var rgb))
            return;
        if (Application.Current.Resources.ThemeDictionaries["Dark"] is not ResourceDictionary dark)
            return;
        var color = ColorHelper.FromArgb(255, rgb.R, rgb.G, rgb.B);
        // Brushes are shared through ThemeResource, so changing their color restyles every control at once.
        if (dark["HeliosAccent"] is SolidColorBrush accentBrush)
            accentBrush.Color = color;
        if (dark["HeliosOnAccent"] is SolidColorBrush onAccent)
            onAccent.Color = ConsoleTheme.PrefersDarkText(rgb) ? ColorHelper.FromArgb(255, 20, 16, 12) : Colors.White;
        if (dark["SystemControlFocusVisualPrimaryBrush"] is SolidColorBrush focus)
            focus.Color = ColorHelper.FromArgb(255, Lighten(rgb.R), Lighten(rgb.G), Lighten(rgb.B));

        static byte Lighten(byte value) => (byte)(value + (255 - value) * 0.35);
    }

    /// <summary>Theme-dependent structure: shelf arrangement and the library grid.</summary>
    private void ApplyLayout()
    {
        var home = HomeTileStyle;
        var grid = GridTileStyle;
        bool wrap = _theme.Layout == HomeLayout.Grid;
        ShelfScroll.HorizontalScrollMode = wrap ? ScrollMode.Disabled : ScrollMode.Enabled;
        ShelfScroll.HorizontalScrollBarVisibility = wrap ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Hidden;
        Shelf.Layout = wrap
            ? new UniformGridLayout { MinItemWidth = home.Width, MinItemHeight = home.Height, MinColumnSpacing = 20, MinRowSpacing = 24 }
            : new StackLayout { Orientation = Orientation.Horizontal, Spacing = _theme.Layout == HomeLayout.Shelves ? 20 : 16 };
        GameGrid.Layout = new UniformGridLayout { MinItemWidth = grid.Width, MinItemHeight = grid.Height, MinColumnSpacing = 20, MinRowSpacing = 24 };
        HeroArea.Visibility = Visible(!wrap);
        HeroDescription.Visibility = Visible(_theme.Layout != HomeLayout.Cinema);
        ScrimSide.Opacity = _theme.Layout == HomeLayout.Cinema ? .55 : 1;
        _backdropOpacity = _theme.Layout switch
        {
            HomeLayout.Spotlight => .7,
            HomeLayout.Cinema => .92,
            HomeLayout.Grid => .35,
            _ => .55,
        };
    }

    /// <summary>Size- and theme-dependent metrics, re-run on every resize.</summary>
    private void ApplyLayoutMetrics()
    {
        double width = _size.Width, height = _size.Height;
        bool narrow = width < 900;
        Hints.Visibility = Visible(_theme.ShowHints && width >= 760);
        Clock.Visibility = Visible(_theme.ShowClock && width >= 700);

        // Header, footer, shelf label and padding take about 221 px plus the focused tile.
        double room = height - 221 - HomeTileStyle.Height * 1.06;
        HeroArea.Height = _theme.Layout == HomeLayout.Shelves ? Math.Clamp(room, 180, 460) : Math.Max(180, room);
        double title = _theme.Layout switch
        {
            HomeLayout.Spotlight => 76,
            HomeLayout.Cinema => 44,
            _ => 56,
        };
        // Shrink big display titles when the hero is short (handhelds) so the source label never clips.
        title = Math.Min(title, Math.Max(34, HeroArea.Height / 4.2));
        HeroTitle.FontSize = narrow ? Math.Min(title, 38) : title;
        HeroTitle.LineHeight = HeroTitle.FontSize * 1.1;
    }

    // ---- Settings: Appearance card ----

    private void BuildAppearance()
    {
        AddOptionRow("Theme", ConsoleTheme.Presets.Select(preset => (preset.Name, preset.Name)).ToList(),
            theme => theme.Name,
            (theme, name) => ConsoleTheme.Presets.First(preset => preset.Name == name)
                with { ShowHints = theme.ShowHints, ShowClock = theme.ShowClock });
        AddOptionRow("Home layout",
            [(HomeLayout.Shelves, "Shelves"), (HomeLayout.Spotlight, "Spotlight"), (HomeLayout.Cinema, "Cinema"), (HomeLayout.Grid, "Grid")],
            theme => theme.Layout, (theme, value) => theme with { Layout = value, Name = "Custom" });
        AddOptionRow("Tiles",
            [(TileShape.Landscape, "Landscape"), (TileShape.Portrait, "Covers"), (TileShape.Square, "Square")],
            theme => theme.Shape, (theme, value) => theme with { Shape = value, Name = "Custom" });
        AddOptionRow("Corners",
            [(CornerStyle.Square, "Sharp"), (CornerStyle.Soft, "Soft"), (CornerStyle.Round, "Round")],
            theme => theme.Corners, (theme, value) => theme with { Corners = value, Name = "Custom" });
        AddAccentRow();
        AddBackgroundRow();
    }

    private void AddOptionRow<T>(string label, IReadOnlyList<(T Value, string Text)> options,
        Func<ConsoleTheme, T> read, Func<ConsoleTheme, T, ConsoleTheme> write)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var buttons = new List<(T Value, Button Button)>();
        foreach (var (value, text) in options)
        {
            var button = new Button { Content = text, Style = (Style)Application.Current.Resources["HeliosPill"] };
            button.Click += (_, _) => ApplyTheme(write(_theme, value), persist: true);
            buttons.Add((value, button));
            row.Children.Add(button);
        }
        _appearanceRenderers.Add(() => RenderOptions(label, buttons, read(_theme)));
        AppearanceRows.Children.Add(OptionGroup(label, row));
    }

    private static void RenderOptions<T>(string label, List<(T Value, Button Button)> buttons, T current)
    {
        foreach (var (value, button) in buttons)
        {
            bool selected = EqualityComparer<T>.Default.Equals(current, value);
            StyleChip(button, selected);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button,
                $"{label}: {button.Content}{(selected ? ", selected" : string.Empty)}");
        }
    }

    private void AddAccentRow()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var swatches = new List<(string Hex, Button Button)>();
        foreach (string hex in ConsoleTheme.AccentSwatches)
        {
            var button = Swatch(hex);
            button.Click += (_, _) => ApplyTheme(_theme with { Accent = hex, Name = "Custom" }, persist: true);
            swatches.Add((hex, button));
            row.Children.Add(button);
        }
        _appearanceRenderers.Add(() => RenderSwatches(swatches, _theme.Accent));
        AppearanceRows.Children.Add(OptionGroup("Accent", row));
    }

    private static void RenderSwatches(List<(string Hex, Button Button)> swatches, string accent)
    {
        foreach (var (hex, button) in swatches)
        {
            bool selected = string.Equals(accent, hex, StringComparison.OrdinalIgnoreCase);
            button.BorderThickness = new Thickness(selected ? 3 : 0);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, $"Accent color {hex}{(selected ? ", selected" : string.Empty)}");
        }
    }

    private static Button Swatch(string hex)
    {
        ConsoleTheme.TryParseColor(hex, out var rgb);
        return new Button
        {
            Style = (Style)Application.Current.Resources["HeliosPill"],
            Width = 44,
            Height = 44,
            MinHeight = 44,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(22),
            Background = new SolidColorBrush(ColorHelper.FromArgb(255, rgb.R, rgb.G, rgb.B)),
            BorderBrush = new SolidColorBrush(Colors.White),
        };
    }

    private void AddBackgroundRow()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var style = (Style)Application.Current.Resources["HeliosPill"];
        var art = new Button { Content = "Game art", Style = style };
        var solid = new Button { Content = "Solid", Style = style };
        var picture = new Button { Content = "Your picture…", Style = style };
        art.Click += (_, _) => ApplyTheme(_theme with { Backdrop = BackdropStyle.GameArt, Name = "Custom" }, persist: true);
        solid.Click += (_, _) => ApplyTheme(_theme with { Backdrop = BackdropStyle.Solid, Name = "Custom" }, persist: true);
        picture.Click += ChoosePictureClicked;
        row.Children.Add(art);
        row.Children.Add(solid);
        row.Children.Add(picture);
        _appearanceRenderers.Add(() =>
        {
            StyleChip(art, _theme.Backdrop == BackdropStyle.GameArt);
            StyleChip(solid, _theme.Backdrop == BackdropStyle.Solid);
            StyleChip(picture, _theme.Backdrop == BackdropStyle.Picture);
        });
        AppearanceRows.Children.Add(OptionGroup("Background", row));
    }

    private static StackPanel OptionGroup(string label, UIElement row)
    {
        var group = new StackPanel { Spacing = 8 };
        group.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = ThemeBrush("HeliosMuted"),
        });
        group.Children.Add(new ScrollViewer
        {
            Content = row,
            HorizontalScrollMode = ScrollMode.Enabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollMode = ScrollMode.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(4, 4, 4, 4),
            Margin = new Thickness(-4, 0, -4, 0),
        });
        return group;
    }

    private void RenderAppearance()
    {
        _renderingAppearance = true;
        foreach (var render in _appearanceRenderers)
            render();
        HintsToggle.IsOn = _theme.ShowHints;
        ClockToggle.IsOn = _theme.ShowClock;
        _renderingAppearance = false;
    }

    private static void StyleChip(Button chip, bool selected)
    {
        chip.Background = selected ? ThemeBrush("HeliosAccent") : ThemeBrush("HeliosChip");
        chip.Foreground = selected ? ThemeBrush("HeliosOnAccent") : ThemeBrush("TextFillColorPrimaryBrush");
    }

    private void HintsChanged(object sender, RoutedEventArgs args)
    {
        if (!_renderingAppearance)
            ApplyTheme(_theme with { ShowHints = HintsToggle.IsOn }, persist: true);
    }

    private void ClockChanged(object sender, RoutedEventArgs args)
    {
        if (!_renderingAppearance)
            ApplyTheme(_theme with { ShowClock = ClockToggle.IsOn }, persist: true);
    }

    private async void ChoosePictureClicked(object sender, RoutedEventArgs args)
    {
        if (_picking)
            return;
        _picking = true;
        try
        {
            var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary, ViewMode = PickerViewMode.Thumbnail };
            foreach (string type in (string[])[".jpg", ".jpeg", ".png", ".webp", ".bmp"])
                picker.FileTypeFilter.Add(type);
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(_window));
            var file = await picker.PickSingleFileAsync();
            if (file is null)
                return;
            if ((await file.GetBasicPropertiesAsync()).Size > MaxPictureBytes)
                throw new InvalidOperationException("Choose a picture smaller than 25 MB.");
            // Keep a private copy so the background survives the original being moved or deleted.
            var folder = await StorageFolder.GetFolderFromPathAsync(_window.DataDirectory);
            var backgrounds = await folder.CreateFolderAsync("backgrounds", CreationCollisionOption.OpenIfExists);
            var copy = await file.CopyAsync(backgrounds, $"background{file.FileType.ToLowerInvariant()}", NameCollisionOption.GenerateUniqueName);
            ApplyTheme(_theme with { Backdrop = BackdropStyle.Picture, Picture = copy.Path, Name = "Custom" }, persist: true);
            Status.Text = "Background picture set";
        }
        catch (Exception error) when (IsLocalError(error))
        {
            Status.Text = $"Could not use that picture. {error.Message}";
        }
        finally
        {
            _picking = false;
        }
    }

    private async void ExportThemeClicked(object sender, RoutedEventArgs args)
    {
        if (_picking)
            return;
        _picking = true;
        try
        {
            var picker = new FileSavePicker { SuggestedFileName = $"{_theme.Name}.helios-theme", SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
            picker.FileTypeChoices.Add("Helios theme", [".json"]);
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(_window));
            var file = await picker.PickSaveFileAsync();
            if (file is null)
                return;
            await FileIO.WriteTextAsync(file, _theme.ToJson());
            Status.Text = $"Theme exported to {file.Name}";
        }
        catch (Exception error) when (IsLocalError(error))
        {
            Status.Text = $"Could not export the theme. {error.Message}";
        }
        finally
        {
            _picking = false;
        }
    }

    private async void ImportThemeClicked(object sender, RoutedEventArgs args)
    {
        if (_picking)
            return;
        _picking = true;
        try
        {
            var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
            picker.FileTypeFilter.Add(".json");
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(_window));
            var file = await picker.PickSingleFileAsync();
            if (file is null)
                return;
            if ((await file.GetBasicPropertiesAsync()).Size > 16 * 1024)
                throw new InvalidOperationException("That file is too large to be a Helios theme.");
            var theme = ConsoleTheme.FromJson(await FileIO.ReadTextAsync(file))
                ?? throw new InvalidOperationException("That file is not a Helios theme.");
            if (theme.Picture is not null && !File.Exists(theme.Picture))
                theme = theme with { Picture = null, Backdrop = BackdropStyle.GameArt };
            ApplyTheme(theme, persist: true);
            Status.Text = $"Theme {theme.Name} applied";
        }
        catch (Exception error) when (IsLocalError(error))
        {
            Status.Text = $"Could not import the theme. {error.Message}";
        }
        finally
        {
            _picking = false;
        }
    }
}
