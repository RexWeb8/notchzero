# NotchZero for Windows

A Dynamic Island for Windows: a small black pill at the top centre of the screen that grows into a
glass panel with music controls, today's prayer times and system stats, plus a little mascot.

## Install

1. Download **NotchZero-Setup.exe** from the latest release.
2. Run it. Windows may show *"Windows protected your PC"* because the app isn't code-signed:
   click **More info → Run anyway**.
3. No admin rights are needed. NotchZero installs just for you and can start with Windows.

## Use

- **Hover** the top centre of the screen (or click the pill) to open the panel. Move away to close it.
- **Music** works with Spotify, Apple Music, YouTube in Chrome or Edge, and most players that show up in
  the Windows volume flyout. Click the progress line to seek.
- **Prayer times** are JAKIM times for your zone, picked from your location when Windows allows it,
  otherwise Kuala Lumpur (WLY01). Pick a zone yourself in **Settings → Choose Prayer Zone**.
  At prayer time the island shows a notification (no azan), and music pauses for a few seconds, then carries on.
- **Share files**: drag files onto the island, or use the share button in the panel, to open the
  Windows Share sheet (Nearby Share, Outlook, WhatsApp and more).
- **Settings** (gear button, or right-click the tray icon): mascot, show the mascot when idle,
  song peek, prayer zone, start with Windows, quit.
- The island hides itself while a full-screen app, video or presentation is in front.

### Mascots

Kapas the Cat, Gebu the Rabbit, Bara the Dragon, Pungguk the Owl and Cil the Mouse Deer.
They blink, dance while music plays, doze off when you're away, and sit on a sejadah at prayer time.

## Build

Every push to `main` builds the installer with GitHub Actions (`.github/workflows/build.yml`) and
publishes it as a release. To build locally on Windows with the .NET 8 SDK:

```
dotnet publish NotchZero.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

Settings, the prayer-time cache and a small log live in `%LOCALAPPDATA%\NotchZero`.
