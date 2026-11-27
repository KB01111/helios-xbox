# Xbox mode home and launcher UI plan

This plan covers two goals. First, Helios must keep working after a restart into Windows Xbox mode (the full screen experience, FSE). Second, Helios should replace the Xbox app as the home that FSE boots into, with a faster and more polished controller UI.

## 1. How Windows picks the Xbox mode home

Windows reads the home app from `HKCU\Software\Microsoft\Windows\CurrentVersion\GamingConfiguration`:

| Value | Meaning |
| --- | --- |
| `GamingHomeApp` | AUMID of the home app. The default is `Microsoft.GamingApp_8wekyb3d8bbwe!Microsoft.Xbox.App`. |
| `StartupToGamingHome` | `1` makes Windows enter Xbox mode at sign-in and start the home app. |

**Settings → Gaming → Xbox mode → Choose home app** only lists packages that declare both of the following:

```xml
<uap3:Extension Category="windows.appExtension">
  <uap3:AppExtension Name="windows.gamingApp" Id="App" DisplayName="Helios Launcher" Description="..." />
</uap3:Extension>
...
<uap4:CustomCapability Name="Microsoft.appCategory.gamingHome_8wekyb3d8bbwe" />
```

The custom capability needs a `CustomCapability.SCCD` file at the package root. Microsoft does not publish a signed SCCD for third parties, so Helios ships a developer SCCD (`AllowAny="true"`, catalog `FFFF`). Windows accepts that SCCD only when **Developer Mode** is on during installation. The package keeps working after Developer Mode is turned back off. Two community projects use the same approach: [FullScreenExperienceShell](https://github.com/driver1998/FullScreenExperienceShell) and [AnyFSE](https://codeberg.org/ashpynov/AnyFSE).

Windows reports Xbox mode state through `api-ms-win-gaming-experience-l1-1-0.dll`, declared in `gamingexperience.h` in the Windows SDK. The DLL exports `IsGamingFullScreenExperienceActive`, `IsGamingFullScreenExperienceSupported`, `CanSetGamingFullScreenExperience`, `SetGamingFullScreenExperience` and `RegisterGamingFullScreenExperienceChangeNotification`.

### Boot and restart behavior to implement

1. **Manifest:** declare `windows.gamingApp`, the `gamingHome` custom capability and the SCCD, so that Helios appears in the home picker.
2. **Installer script** (`scripts/Install-Helios.ps1`):
   - Trust the preview certificate.
   - Turn on Developer Mode temporarily if needed, install the MSIX, then restore the previous Developer Mode setting.
   - With `-SetAsXboxHome`, write `GamingHomeApp` for the user. With `-StartInXboxMode`, also set `StartupToGamingHome`.
   - `-RestoreXboxApp` reverses those settings.
3. **Runtime:**
   - Subscribe to Xbox mode change notifications, and switch to full screen when Xbox mode turns on.
   - Return to the saved window mode when Xbox mode turns off.
   - Treat the Game Bar Home button as a bring-to-front. Single-instance redirection already handles it.
4. **Exit safely:** when Helios is the home, closing it would leave an empty Xbox mode session. In Xbox mode, the header's exit button becomes **Desktop**, which calls `SetGamingFullScreenExperience(false)`.
5. **Settings:** show live status for Xbox mode (on or off), the home app (Helios or another app), and sign-in to Xbox mode, plus buttons to fix each one.
6. **Diagnostics:** `activation.log` records the activation kind, the Xbox mode state and whether Helios is the home app.

## 2. UI framework analysis

| Option | Startup and runtime | Controller focus | Windows integration | Verdict |
| --- | --- | --- | --- | --- |
| **WinUI 3 (C#, current)** | Native DirectComposition. The cold window takes about 1.4 s today and ReadyToRun improves it. Implicit Composition animations run off the UI thread. | Built-in XY focus engine. Gamepad keys flow through it, and Narrator support comes free. | First class: `AppWindow` full screen, `AppInstance` single instance, `StartupTask`, `PackageManager`, MSIX. | **Keep.** It is the lowest-risk path to a polished, fast console UI. |
| WPF (.NET) | Mature but DX9-era rendering, with weaker composition animations. | Basic. | Needs extra bridging for MSIX and WinRT. | No |
| Tauri 2 (Rust + WebView2) | About 300 ms to the first window, plus WebView2 memory (about 100 MB or more). Web UI is flexible for design. | Gamepad support through the web Gamepad API, but focus handling must be custom. | Good through Rust crates, but adds a second runtime. | Viable for a web-skinned UI, but heavier than needed |
| **Slint (Rust)** | Native GPU renderer, very fast start, small memory. | Keyboard focus is built in. You write XY gamepad navigation yourself (with gilrs or XInput). | Through `windows-rs`. MSIX packaging works, but you hand-write single instance and startup task code. | **Best Rust option.** Good for a later rewrite or a low-power handheld build |
| egui / iced (Rust) | Fast, immediate or Elm style. | Little built-in focus or accessibility. | Through `windows-rs`. | Not polished enough for a console shell |
| WinUI 3 from Rust (`windows-rs`) | Native. | Same as WinUI. | No XAML compiler or `x:Bind` and no official tooling. | Not practical today |

**Can Rust be used?** Yes. Slint plus `windows-rs` can build a fully native, very fast launcher, and Windows FSE only cares about the MSIX manifest, which a Rust app can ship too. A rewrite would trade WinUI's focus engine, accessibility, packaging tooling and the existing tested code for a smaller binary. That is not a good trade for this milestone. The practical split is:

- **Now:** keep WinUI 3 for the shell and make it snappier with cached image decode, implicit animations, ReadyToRun and no layout thrash.
- **Later, optional:** move scanning or artwork processing into a Rust library behind a C ABI if profiling shows it is hot. Or prototype a Slint shell against the same `library.db` schema for low-power handhelds.

## 3. Launcher UI redesign

The current UI is a desktop-style page: a header bar, a 276 px hero card and small 226×159 tiles, with text buttons for actions. It works, but it does not feel like a console home. Its weak points:

- Focus has no motion or scale, so selection is hard to read from a couch.
- The hero is fixed to one game, so the screen does not react to what is focused.
- The Library has two rows of plain buttons before any games appear.
- Images are re-decoded every time a view refreshes.

### Target design

- **Ambient backdrop:** a full-bleed, dimmed and blurred copy of the focused game's art sits behind everything. It crossfades in 180 ms on focus change.
- **Top bar:** the HELIOS mark, pill tabs (Home, Library, Settings) with an accent underline, and a clock with an Xbox mode badge on the right.
- **Home:**
  - The hero area at the top shows the focused game's source, title and status, with **Play** and **Details** buttons.
  - Under it, a "Continue" shelf of large 16:9 tiles: recent games first, then favorites, then the rest.
  - A "Favorites" shelf follows.
  - Moving across the shelf updates the hero and backdrop.
- **Tiles:** 300×169 with rounded corners. The focused tile scales 1.08×, gains an accent ring and lifts its shadow through implicit `ScaleTransition` and Opacity animations of about 120 ms. Favorites carry a star badge, and unavailable games are dimmed.
- **Library:**
  - Filter chips (All, Steam, Xbox & Store, Manual, Favorites) that LB and RB cycle with the triggers, plus search on X.
  - A responsive grid.
  - The add actions sit in a compact trailing chip row.
- **Details:** the full-bleed hero art with the title, a meta line and large action buttons.
- **Footer hints:** glyph badges for A Select, B Back, Y Favorite, X Search and LB/RB Tabs. Each hint shows only when that action is valid.
- **Speed:**
  - Decoded bitmaps are cached per path and size.
  - Pane switches only toggle visibility, with a 120 ms entrance fade.
  - Rendering stays in the XAML compositor, with no per-frame code-behind.
  - The library still loads from cache first and refreshes in the background.

### Controller map

| Input | Action |
| --- | --- |
| D-pad or left stick | Move focus |
| A | Select or confirm |
| B | Back |
| X | Search (Library) |
| Y | Favorite the focused game |
| LB / RB | Switch tabs |
| LT / RT | Cycle library filters |
| Menu | Settings |

## 4. Work items

1. Change the manifest (gamingApp extension, custom capability) and add the SCCD as package content.
2. Extend `XboxMode` with Supported, CanSet and Set, change notifications, and home-app and startup registry reads.
3. Add Xbox mode reactions in `MainWindow` and `Dashboard`, including the safe exit.
4. Write `scripts/Install-Helios.ps1` (install, set as home, restore).
5. Add `Helios.Library.LibraryViews` with the shelf ordering and filtering logic, plus unit tests.
6. Redesign the XAML: backdrop, hero, shelves, tiles, chips, hints and the image cache.
7. Extend the controller to X, Y, Menu and the triggers.
8. Update the README and the compatibility notes. Then build, test, run on this PC and verify that Helios is listed in the `windows.gamingApp` catalog.
