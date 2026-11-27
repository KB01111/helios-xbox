<img src="src/Helios.Launcher/Assets/StoreLogo.png" width="50" height="50" alt="">

# Helios Launcher

Helios is a controller-first launcher for Windows 11. It brings installed Steam and Xbox or Microsoft Store games, local executables, and shortcuts into one full-screen library. Favorites and recent launches stay on your PC.

## Install the Windows preview

Download the x64 MSIX and its public preview certificate from [GitHub Releases](https://github.com/KB01111/helios-xbox/releases). Helios needs Windows 11 24H2 or later. Install it from an elevated PowerShell prompt, signed in as the person who will play:

~~~powershell
.\scripts\Install-Helios.ps1 -Package .\Helios-Launcher-0.2.0-x64.msix -Certificate .\Helios-Preview.cer -SetAsXboxHome -StartInXboxMode
~~~

The script trusts the preview certificate and installs the package. Helios declares the Xbox mode gaming home capability, which Windows accepts only while Developer Mode is on. The script turns Developer Mode on for the install and then restores your previous setting. `-SetAsXboxHome` makes Helios the Xbox mode home, and `-StartInXboxMode` starts Windows in Xbox mode at sign-in. Leave both out to install Helios as a regular app.

## Use Helios as the Xbox mode home

After installation, Helios appears in **Settings → Gaming → Xbox mode → Choose home app**. With Helios selected and **Enter Xbox mode on startup** turned on, a restart signs in straight into Helios instead of the Xbox app, and the Game Bar **Home** button returns to Helios. In Xbox mode, Helios stays full screen, and the top-right button returns to the Windows desktop instead of closing Helios. **Settings → Xbox mode** in Helios shows the current home app and startup state.

To go back to the Xbox app, run `.\scripts\Install-Helios.ps1 -RestoreXboxApp` or choose Xbox in Windows Settings. Helios stays installed. See the [compatibility notes](docs/xbox-mode-compatibility.md) for details and rollback.

## Controls

| Controller | Keyboard | Action |
| --- | --- | --- |
| D-pad or left stick | Arrow keys | Move |
| A | Enter or Space | Select |
| B | Escape | Back |
| X | | Search the library with the on-screen keyboard |
| Y | | Favorite the focused game |
| LB / RB | | Switch tabs |
| LT / RT | | Change the library filter |
| Menu | | Settings |
| | F11 | Full screen on the desktop |

**Library → Add file** adds a game executable or shortcut. **Installed app** adds a registered Windows title. Turn on **Settings → Start Helios at Windows sign-in** to open Helios after a regular desktop sign-in. The package also registers `helios-launcher://home` for launch or bring-to-front.

## Build

Use the .NET 10 SDK and Windows 11 x64. The project restores the Windows App SDK and build tools from NuGet.

~~~powershell
dotnet build src/Helios.Launcher/Helios.Launcher.csproj -c Release -p:Platform=x64
dotnet test tests/Helios.Library.Tests/Helios.Library.Tests.csproj -c Release -p:Platform=x64
dotnet run --project src/Helios.Launcher/Helios.Launcher.csproj -c Debug -p:Platform=x64 -p:SelfContained=true
~~~

The run command registers a development package. That registration includes the gaming home capability, so Developer Mode must be on. To make an installer with Microsoft's Windows App Development CLI, run the following commands from the repository root.

~~~powershell
New-Item -ItemType Directory -Force artifacts | Out-Null
winapp cert generate --manifest src/Helios.Launcher/Package.appxmanifest --output artifacts/Helios-Preview.pfx --export-cer --if-exists Skip
winapp package src/Helios.Launcher/Helios.Launcher.csproj -c Release --arch x64 --self-contained -p SelfContained=true --cert artifacts/Helios-Preview.pfx --output artifacts/Helios-Launcher-0.2.0-x64.msix
~~~

Keep the PFX locally. Distribute the MSIX and, for private preview builds, its public CER.

The UI framework choice, the Rust evaluation and the Xbox mode home design are in [docs/superpowers/plans/2026-11-27-xbox-home-and-launcher-ui.md](docs/superpowers/plans/2026-11-27-xbox-home-and-launcher-ui.md).
