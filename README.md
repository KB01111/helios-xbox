<img src="src/Helios.Launcher/Assets/StoreLogo.png" width="50" height="50" alt="">

# Helios Launcher

Helios is a controller-first launcher for Windows 11. It brings installed Steam and Xbox or Microsoft Store games, local executables, and shortcuts into one full-screen library. Favorites and recent launches stay on your PC.

## Install the Windows preview

Download the x64 MSIX and its public preview certificate from [GitHub Releases](https://github.com/KB01111/helios-xbox/releases). On Windows 11 24H2 or newer, open the certificate and install it under **Local Machine → Trusted People**, then open the MSIX. Installing a preview certificate requires a Windows administrator. The certificate identifies this self-signed preview.

Open **Helios Launcher** from Start. Use **Library → Add file** for a game executable or shortcut, or **Add installed app** to pick a registered Windows title. A or Enter selects, B or Escape goes back, and the shoulder buttons switch tabs. F11 changes the window mode.

## Launch at sign-in and use Xbox mode

Turn on **Settings → Start Helios at sign-in** for the Windows startup path. The first launch opens full screen when Windows delivers startup activation. To disable it, turn off the setting in Helios or open **Windows Settings → Apps → Startup**.

Helios can run as an app inside Xbox mode. Use **Settings → Open Xbox mode settings** to see the options available on your PC. Windows selects the gaming home and routes the Game Bar Home button. The preview is a standard MSIX, so Xbox mode home replacement depends on the device's available integration. See the [compatibility notes](docs/xbox-mode-compatibility.md) for setup and rollback.

The package registers `helios-launcher://home` for launch or bring-to-front. This is also the entry point for a separately configured launcher bridge.

## Build

Use the .NET 10 SDK and Windows 11 x64. The project restores the Windows App SDK and build tools from NuGet.

~~~powershell
dotnet build src/Helios.Launcher/Helios.Launcher.csproj -c Release -p:Platform=x64
dotnet test tests/Helios.Library.Tests/Helios.Library.Tests.csproj -c Release -p:Platform=x64
dotnet run --project src/Helios.Launcher/Helios.Launcher.csproj -c Debug -p:Platform=x64 -p:SelfContained=true
~~~

The run command registers a development package. To make an installer with Microsoft's Windows App Development CLI, run the following commands from the repository root.

~~~powershell
New-Item -ItemType Directory -Force artifacts | Out-Null
winapp cert generate --manifest src/Helios.Launcher/Package.appxmanifest --output artifacts/Helios-Preview.pfx --export-cer --if-exists Skip
winapp package src/Helios.Launcher/Helios.Launcher.csproj -c Release --arch x64 --self-contained -p SelfContained=true --cert artifacts/Helios-Preview.pfx --output artifacts/Helios-Launcher-0.1.0-x64.msix
~~~

Keep the PFX locally. Distribute the MSIX and, for private preview builds, its public CER.
