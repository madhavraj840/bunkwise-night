# Night mode for Windows

**Night mode makes your laptop screen darker than its own lowest brightness.** It's a free, open-source tray app for Windows 10 and 11, and it needs no account and shows no ads.

It first turns the real backlight down to the hardware minimum. Then it keeps dimming on screen, down to 4% of normal brightness. Windows' own brightness slider can't go that low.

Website: **[nightmode.bunkwise.in](https://nightmode.bunkwise.in)**. There is also an Android version: **[Night mode on Google Play](https://play.google.com/store/apps/details?id=com.bunkwise.nightmode)**.

<p>
  <img src="docs/img/win-flyout.webp" alt="Night mode panel with the dim scale, presets and colour options" width="300">
  <img src="docs/img/win-settings.webp" alt="Night mode settings window, Dimming page" width="480">
</p>

## Download

Two versions are available, with the same app in each:

| Download | Size | Use it when |
|---|---|---|
| **[NightMode-1.0.0-win-x64-full.zip](https://github.com/madhavraj840/bunkwise-night/releases/download/v1.0.0/NightMode-1.0.0-win-x64-full.zip)** | 61 MB | You aren't sure which to pick. It runs on any 64-bit Windows 10 or 11 PC with nothing else to install. |
| [NightMode-1.0.0-win-x64.zip](https://github.com/madhavraj840/bunkwise-night/releases/download/v1.0.0/NightMode-1.0.0-win-x64.zip) | 0.4 MB | You already have the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) installed. |

All versions are listed on the **[Releases page](https://github.com/madhavraj840/bunkwise-night/releases/latest)**.

SHA-256 checksums for v1.0.0:

```
d18b77f56e8ccf3d5505aaf729c4ae648116b874cf876535c3115d3b4139cfb7  NightMode-1.0.0-win-x64-full.zip
5e8caa85bf063343f856e4addb3d84764a95917335e047e45eae35363ff94799  NightMode-1.0.0-win-x64.zip
```

## Install

1. **Download** one of the zips above.
2. **Unzip it:** right-click the zip, choose **Extract All…**, then **Extract**. You can move the folder anywhere you like, for example into Documents.
3. **Run** `NightMode.exe`. You may see **"Windows protected your PC"**. This happens because the app is new and not code-signed yet. Click **More info**, then **Run anyway**. Windows only asks once.
4. **Find it in the tray:** Night mode lives in the system tray next to the clock. If you don't see the moon icon, click the **^** arrow. Click the icon to open the panel.
5. **Optional:** open **Settings → Dimming → Start with Windows** so the app is ready every time you sign in.

To uninstall, quit the app from its tray menu and delete the folder. Your settings are stored in `%APPDATA%\NightMode`, which you can delete too.

## How it dims

| Scale | What happens |
|---|---|
| 0–40 | The laptop's real backlight goes down, from your current level to the hardware minimum. This also saves battery. |
| 40–100 | The **sub-zero** range: a full-screen colour filter dims the screen further, down to 4% of normal brightness. It's never fully black. |

- **No backlight control:** on a desktop or external monitor, the colour filter covers the whole scale instead.
- **Overlay method:** if the colour filter isn't available, or you pick **Overlay** in Settings, a click-through overlay is used instead.
- **Crashes:** if the app crashes, Windows removes the filter, and the next launch restores your backlight.

## Features

- Presets: Evening 30, Reading 50, Dark room 70, Pitch black 90, plus your own
- Colour: Natural, Warm (amber) or Grey
- Schedule, with a gradual ease-in
- App rules: dimming pauses while a chosen app is in front
- Full brightness for 30 seconds, 1 minute or 5 minutes, after which dimming comes back on its own
- Remember after restart and Start with Windows (both off by default)

## Shortcuts

| Keys | Action |
|---|---|
| `Ctrl + Alt + D` | Turn on or off |
| `Ctrl + Alt + −` / `+` | Darker / brighter |
| `Ctrl + Alt + S` | Screenshot at normal brightness |
| `Ctrl + Alt + Backspace` | **Full brightness (emergency)** |

`Ctrl + Alt + Backspace` works even when the screen is too dark to see.

## Privacy

Night mode works fully offline. It has no account, no ads and no analytics, and it doesn't make network connections. Your settings stay on your PC.

## Build from source

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0). The app has no NuGet packages.

```powershell
dotnet build
dotnet run --project checks   # dimming-maths checks

# full download (no runtime needed)
dotnet publish NightMode.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none

# small download (needs the .NET 10 Desktop Runtime)
dotnet publish NightMode.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:DebugType=none
```

## Requirements

- Windows 10 or 11, 64-bit
- Backlight control needs a laptop or another panel that Windows can dim. On other screens, the colour filter does all the dimming.

## Licence

[MIT](LICENSE) © 2026 BunkWise Technologies. The bundled Geist fonts are under the [SIL Open Font License](Assets/Fonts/OFL.txt).

Night mode is a screen dimmer, not a medical device.
