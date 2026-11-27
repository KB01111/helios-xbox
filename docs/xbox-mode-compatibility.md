# Windows Xbox mode compatibility

Helios is an x64 desktop app for Windows 11 24H2 or later. The features exposed by Windows under **Settings → Gaming → Xbox mode** vary by form factor, market, and OS build. [Microsoft Xbox mode setup](https://support.microsoft.com/en-us/servicing/os/windows/docs/2025/10/windows-gaming-full-screen-experience)

| Path | Availability and expected behavior |
| --- | --- |
| Desktop sign-in | Helios registers a disabled-by-default startup task. Enable it in Helios or Windows Settings. It opens in the signed-in user's session. |
| Xbox mode startup app | If Windows offers **Start at log in** for Helios, test whether it becomes interactive inside Xbox mode at sign-in. Windows retains the configured gaming home for Game Bar. |
| Native gaming home | Sign-in and Game Bar Home depend on Windows listing Helios in the home picker. The preview package uses standard desktop app capabilities. Microsoft documents an OEM image configuration for assigning a third-party launcher. [OEM configuration](https://learn.microsoft.com/en-us/windows-hardware/customize/desktop/unattend/microsoft-windows-gaming-configuration-gaminghomeapp) |
| Optional bridge | A community home app could hand off to Helios. The package exposes `helios-launcher://home`. Check bridge support for that target, verify prerequisites, use a test system, and exercise rollback. [AnyFSE project](https://github.com/ashpynov/AnyFSE) |

## Preview verification

- On Windows 11 26H2 build 26300.10075, the registered development package opened the dashboard in 1.4 seconds from process start. Steam and Xbox or Store discovery, favorites, Steam client handoff, Windows app launch, same-window protocol activation, and enabling and disabling the startup task were exercised on that build.
- Windows declined the launch of an installed Xbox game on this PC. Opening the same title from its Windows shortcut also failed to bring up the game. A separate Microsoft Store app opened from Helios using the same launch API.
- The x64 installer includes the .NET and Windows App SDK runtimes and uses a self-signed preview certificate. Installation with a trusted preview certificate, cold sign-in, native Xbox mode home selection, Game Bar Home, bridge compatibility, and physical controller and resume behavior still need hardware qualification.

## Verify your setup

1. Record the Windows build and the controls exposed by Xbox mode settings. Record the current gaming home and startup options before changing them.
2. From a cold sign-in, check which dashboard opens first and whether the controller responds. Inside Xbox mode, open Game Bar and check what **Home** returns to.
3. Launch an installed game, close it, and return to the same Helios window. Check controller reconnect and sleep/resume on the hardware you intend to use.
4. To roll back, disable Helios in **Apps → Startup**. If you selected a different home app in Xbox mode settings, restore Xbox there. Use Game Bar, Task View, or Win+F11 to return to the desktop.

Helios writes the window startup timing and the reported Xbox mode state to `activation.log` in the package's LocalState folder. The startup task runs after Windows sign-in. User authentication and a working Windows desktop remain available.
