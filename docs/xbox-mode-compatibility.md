# Windows Xbox mode compatibility

Helios is an x64 desktop app for Windows 11 24H2 or later. The options under **Settings → Gaming → Xbox mode** depend on the form factor, market and OS build. [Microsoft Xbox mode setup](https://support.microsoft.com/en-us/servicing/os/windows/docs/2025/10/windows-gaming-full-screen-experience)

## How Helios becomes the Xbox mode home

Windows lists an app under **Choose home app** when its package declares two things:

- the `windows.gamingApp` app extension
- the `Microsoft.appCategory.gamingHome_8wekyb3d8bbwe` custom capability

Helios declares both in `Package.appxmanifest` and ships the capability descriptor as `CustomCapability.SCCD`. The descriptor is a developer descriptor (catalog `FFFF`), so Windows accepts it only while Developer Mode is on during installation. `scripts/Install-Helios.ps1` turns Developer Mode on for the install and then restores the previous setting. The installed package keeps working after Developer Mode is turned off.

The selection is stored per user in `HKCU\Software\Microsoft\Windows\CurrentVersion\GamingConfiguration`:

| Value | Meaning |
| --- | --- |
| `GamingHomeApp` | AUMID of the home app. For Helios this is `KB01111.HeliosLauncher_<publisher id>!App`. The Xbox app is `Microsoft.GamingApp_8wekyb3d8bbwe!Microsoft.Xbox.App`. |
| `StartupToGamingHome` | `1` makes Windows start in Xbox mode at sign-in. |

Choosing a home in Windows Settings writes these values, and so does the install script with `-SetAsXboxHome` and `-StartInXboxMode`. Helios reads them, but writes from inside the package would be virtualized, so Helios does not change them.

## Runtime behavior

| Situation | Behavior |
| --- | --- |
| Restart with Helios as home and Xbox mode at startup | Windows signs in to Xbox mode and starts Helios full screen. `activation.log` records `xboxMode=active home=helios`. |
| Game Bar **Home** | Windows activates Helios again. The single-instance redirect brings the existing window to the front in full screen. |
| Xbox mode turns on or off while Helios runs | Helios subscribes to `RegisterGamingFullScreenExperienceChangeNotification`. It goes full screen in Xbox mode and returns to the saved desktop window mode afterwards. |
| Leaving Helios in Xbox mode | The top-right button becomes **Desktop** and calls `SetGamingFullScreenExperience(false)`, so you don't end up in an empty session. Escape and F11 do not leave full screen in Xbox mode. |
| Desktop sign-in | The opt-in startup task, **Settings → Start Helios at Windows sign-in**, opens Helios full screen. |

## Staying light while a game runs

As the Xbox mode home, Helios stays running for the whole session. Three seconds after another app or game takes the foreground, Helios:

- stops its clock timer and controller polling
- drops its cached artwork
- switches to Windows efficiency mode (EcoQoS) at below-normal priority
- releases its working set

When Helios comes back to the front, it returns to normal priority straight away. On the development PC, the working set went from 193 MB in the foreground to 3 MB in the background, and back to 28 MB on return. Helios skips this while one of its own file pickers is open. It also honors **Settings → Accessibility → Visual effects → Animation effects**: with animations off, focus scaling, page transitions and backdrop crossfades switch instantly.

## Verification on the development PC

On Windows 11 build 26300 with Developer Mode on, the registered development package appears in the `windows.gamingApp` app extension catalog alongside `Microsoft.GamingApp_8wekyb3d8bbwe!Microsoft.Xbox.App`. That catalog is the source of the **Choose home app** list. The Release build reaches the loaded dashboard in about 1.2 seconds from process start.

A cold restart into Xbox mode with Helios selected needs testing on the target hardware. So do controller reconnect, and sleep and resume, inside Xbox mode.

## Verify your setup

1. Install with `scripts/Install-Helios.ps1`. Open **Settings → Gaming → Xbox mode** and confirm that Helios appears under **Choose home app**.
2. Select Helios, turn on **Enter Xbox mode on startup**, and restart. Helios should be the first screen, and the controller should respond.
3. Open Game Bar and press **Home**. You should return to the same Helios window.
4. Launch a game, close it, and confirm that you land back in Helios.
5. Check `activation.log` in the package's LocalState folder for the activation kind, the Xbox mode state and the home app.

## Roll back

- Run `scripts/Install-Helios.ps1 -RestoreXboxApp`, or choose Xbox in **Settings → Gaming → Xbox mode → Choose home app**.
- Use **Desktop** in Helios, Game Bar, Task View or Win+F11 to reach the Windows desktop.
- Uninstalling Helios from **Apps** also removes it from the home picker.

Community projects such as [AnyFSE](https://codeberg.org/ashpynov/AnyFSE) and [FullScreenExperienceShell](https://github.com/driver1998/FullScreenExperienceShell) use the same extension and capability.
