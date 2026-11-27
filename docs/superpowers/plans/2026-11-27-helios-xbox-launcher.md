# Helios Xbox Launcher Implementation Plan

Build a Windows 11 game launcher that opens to a custom, controller-first dashboard after sign-in. The primary goal is for Windows Xbox mode to open Helios as its gaming home and return to it from Game Bar. The first milestone tests that behavior on the target PC with a minimal packaged app.

## Platform feasibility

Microsoft documents the **Choose home app** and **Enter Xbox mode on startup** controls for gaming handhelds. Its desktop, laptop, and tablet instructions describe enabling Xbox mode and entering through Game Bar, the Xbox app, Task View, or a shortcut. Confirm which controls are present on the target PC. Xbox mode requires Windows 11 24H2 or later and availability in a supported market. Startup occurs after Windows sign-in. [Microsoft Xbox mode setup](https://support.microsoft.com/en-us/servicing/os/windows/docs/2025/10/windows-gaming-full-screen-experience)

Microsoft documents an OEM image setting that accepts a third-party launcher's app identity. Its public gaming APIs let apps read Xbox mode state and subscribe to changes. I found no documented retail installation API that makes an arbitrary third-party MSIX eligible for the gaming home picker. [OEM GamingHomeApp setting](https://learn.microsoft.com/en-us/windows-hardware/customize/desktop/unattend/microsoft-windows-gaming-configuration-gaminghomeapp) · [Xbox mode developer guidance](https://learn.microsoft.com/en-us/gaming/gdk/docs/gdk-dev/pc-dev/handheld/handheld-launchers?view=gdk-2604)

| Boot path | Observable result |
| --- | --- |
| Native gaming home | Windows selects Helios as the home app and opens it at Xbox mode sign-in. Game Bar Home returns to Helios. Ship as native integration only after testing all three. |
| Community bridge | Windows selects the bridge and the bridge opens Helios inside Xbox mode. Evaluate an existing project such as AnyFSE on a test system if the native path is unavailable and the user wants the exact experience. Review installer actions, privileges, update compatibility, and rollback before testing. Keep the bridge optional and version-specific. [AnyFSE project](https://github.com/ashpynov/AnyFSE) |
| Xbox mode startup app | Where Windows exposes **Start at log in** for an Xbox mode startup app, test whether Helios starts in that session and receives foreground focus. The selected gaming home still owns the Game Bar Home action. [Microsoft startup behavior](https://support.microsoft.com/en-us/servicing/os/windows/docs/2025/10/windows-gaming-full-screen-experience) |
| Desktop sign-in | Windows launches Helios full screen from an opt-in startup entry. This delivers the controller dashboard after sign-in on systems where Xbox mode startup is unavailable. [Microsoft StartupTask documentation](https://learn.microsoft.com/en-us/uwp/api/windows.applicationmodel.startuptask) |

## First release

- **Home** opens with the last launched game, a clear Play action, and rows for favorites and recently launched games. Newly installed users land on the library or an Add Game prompt.
- **Library** shows installed Steam games, validated Xbox or Microsoft Store entries, and manually added games. It supports search, source filters, and favorites. Details show the title, source, artwork, and launch status.
- **Navigation** uses a bright focus outline and restores the last focused tile when returning from details or a game. The D-pad and left stick navigate, A confirms, B goes back, and shoulder buttons switch between Home and Library. Keyboard, mouse, and touch remain usable. Provide a controller-operable search keyboard if the system keyboard is unavailable in Xbox mode.
- **Settings** exposes display mode, opt-in sign-in launch, Xbox mode setup, and an easy exit. Xbox mode setup opens Windows Settings. The original visual direction uses graphite panels, warm focus accents, and large game art suited to both TVs and handheld screens.
- Store clients continue to handle downloads, updates, sign-in, entitlement, cloud saves, and any game-specific prompts. Epic, GOG, and Battle.net can be added manually through shortcuts in the first release. Automated imports for those stores can follow.

## Architecture

Start with x64 Windows 11, C#, WinUI 3, the stable Windows App SDK, and a per-user MSIX with a stable package identity. Use `AppWindow` for borderless full screen and `AppInstance` to redirect launches to the existing window. A small XInput adapter handles Xbox-compatible controllers and feeds the same navigation actions as keyboard input. Windows handles the Guide button in Xbox mode. [Full-screen presentation](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.windowing.fullscreenpresenter) · [Single-instance activation](https://learn.microsoft.com/en-us/windows/apps/develop/launch/multi-instance-apps) · [XInput](https://learn.microsoft.com/en-us/windows/win32/xinput/getting-started-with-xinput) · [Guide button behavior](https://learn.microsoft.com/en-us/gaming/gdk/docs/gdk-dev/pc-dev/handheld/handheld-launchers?view=gdk-2604#game-overlays-and-guide-button)

Keep the launcher a regular user app. Use one main window with in-window dialogs because Xbox mode displays one window at a time. Keep Xbox mode onboarding and diagnostics in Windows-specific code. The same library and UI serve every boot path. Store settings, favorites, recents, and the last successful library scan in local SQLite, and cache artwork separately. Show the cached library first and refresh it in the background. [Xbox mode window behavior](https://support.microsoft.com/en-us/servicing/os/windows/docs/2025/10/windows-gaming-full-screen-experience)

| Planned path | Responsibility |
| --- | --- |
| `src/Helios.Launcher/` | WinUI pages, display placement, focus and settings |
| `src/Helios.Launcher/Windows/` | Activation, startup, Xbox mode state, controller adapter, launch dispatch |
| `src/Helios.Library/` | Game entries, provider scans, SQLite, cached artwork references |
| `src/Helios.Launcher/Package.appxmanifest` | Package identity and opt-in startup declaration |
| `tests/Helios.Library.Tests/` | Provider fixtures, persistence, target validation, and refresh behavior |
| `docs/xbox-mode-compatibility.md` | Tested Windows builds, boot behavior, setup, and rollback |

Providers return entries with a stable provider-specific ID, title, optional artwork, availability, and a typed target. Dispatch accepts a Steam App ID, a Windows app identity, or a user-selected executable or shell shortcut. Keep scanned metadata separate from user preferences so reinstalling or reconnecting a drive restores the existing favorites. Record recent launches as OS- or storefront-accepted requests and track the actual game lifecycle only when there is a reliable signal.

## Build sequence

### 1. Prove the boot path on the target PC

- [ ] Record Windows version and build, form factor, and the available controls in **Settings → Gaming → Xbox mode**. Open the documented `ms-settings:gaming-fullscreen` settings page during the test. [Windows Settings URI](https://learn.microsoft.com/en-us/windows/apps/develop/launch/launch-settings)
- [ ] Package a single full-screen WinUI window with one controller-selectable action and an exit. Add a disabled-by-default startup task. Log activation time and Xbox mode state locally. Treat unavailable mode APIs as unknown and confirm active mode with the Windows UI.
- [ ] Install and check the home picker. If Helios is eligible, select it and test Xbox mode at sign-in plus the Game Bar Home action. If it is absent, test **Start at log in** where offered, then verify regular desktop sign-in independently. If true Xbox mode home is essential and the direct path is absent, evaluate the optional bridge on a test profile or test system before proceeding with that claim.
- [ ] Repeat cold sign-in, manual Xbox mode entry and exit, and sleep and resume. Record which app is first interactive, whether the Xbox app appears first, whether the controller works immediately, and where Game Bar Home goes. A bridge handoff during manual entry earns Xbox mode sign-in support only if cold sign-in also reaches it. Confirm a way back to desktop and restore the previous Windows home and startup settings.

**Milestone:** A reproducible result for the target PC identifies the available boot path. If none of the Xbox mode paths meet the goal, the decision is whether the desktop sign-in experience is worth building for that PC.

### 2. Build the dashboard and controller loop

- [ ] Build Home, Library, Details, and Settings around the minimal app. Add stable tile focus, navigation prompts, readable text at TV distance, high-contrast focus, and reduced motion.
- [ ] Connect XInput while Helios is active. Handle stick dead zones, repeat timing, button press edges, and wireless reconnection. Stop handling navigation while another window has focus and require a fresh button press when focus returns.
- [ ] Add startup setting status, a windowed escape, and a way to close the launcher and use the Windows desktop. In Xbox mode expose the Windows-documented exit through Game Bar or Task View. Restore one active window on repeated activation and fall back to the primary display if the saved monitor is disconnected.

**Milestone:** An Xbox controller alone can open the library, search, choose a title, go back, and exit. The test covers a TV and a handheld-sized layout.

### 3. Discover and launch real games

- [ ] Import installed Steam games from the client's library manifests and hand off launches to Steam. Test a second drive, a missing client, and a malformed or temporarily inaccessible manifest without losing previously known titles.
- [ ] Discover eligible installed Xbox and Microsoft Store entries through Windows shell registrations. Start verified titles through Windows activation using their app identity or their installed shortcuts. Offer a manual picker for registered games that cannot be identified automatically. Verify at least one installed PC Game Pass title and one Microsoft Store game with existing user entitlements. [Windows app activation](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-iapplicationactivationmanager-activateapplication)
- [ ] Add a manual executable or shortcut picker, favorites, recents, filters, and local artwork. Show errors with a retry or repair action when a game moved, its client is missing, or activation fails. Prevent repeated presses from opening multiple launch requests and keep the dashboard available for the OS app switcher.
- [ ] Verify a full launch and return for Steam, Xbox or Store, and a local executable. For games that start through a storefront or bootstrapper, test return through Task View in Xbox mode and through the desktop app switcher in desktop mode.

**Milestone:** The library works offline from its cache. Installed games that support offline play can still launch under their store's rules. Closing or switching away from a tested game restores the dashboard with focus on that game's tile.

### 4. Package and qualify the release

- [ ] Produce a signed x64 MSIX. Verify clean install, upgrade with existing library data, uninstall, and how to turn off launch at sign-in from Windows settings. Check recovery after a forced launcher crash and after resetting the selected home app to Xbox.
- [ ] On the actual target PC, repeat three cold sign-ins for each advertised path, then test Game Bar, a Bluetooth reconnect, suspend/resume, and an external display. Measure process launch to first controller-ready screen with a goal of three seconds on the target PC, and record sign-in to dashboard separately.
- [ ] Document the tested Windows builds and form factors, the exact first screen and Game Bar Home behavior for each supported path, and setup and rollback. If a community bridge is used, retest its pinned version after Windows updates before extending the compatibility claim.

**Release acceptance:** Helios opens automatically after sign-in on the verified boot path. The user can pick and launch a real game with a controller, return to Helios, and get back to Windows even if the launcher fails. Claim native Xbox mode replacement only when Windows selects and opens Helios directly and Game Bar Home returns to it.
