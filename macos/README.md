# NotchZero

A minimal, white-on-glass notch app: music (Spotify + Apple Music), calendar, CPU / RAM / battery, prayer times with a mascot (Kapas the cat, Gebu the rabbit, Bara the dragon, Pungguk the owl or Cil the mouse deer), and an AirDrop drop zone.

## Using it
- **Hover** over the notch to open the panel. Move away to close it.
- **Drag a file** onto the notch, release it, and pick a nearby device in the AirDrop window.
- **Antenna button** (top right of the panel): choose files to AirDrop.
- **Gear button**: Notch Style (Liquid Glass / Frosted Glass / Solid Black), Mascot (Kapas / Gebu / Bara / Pungguk / Cil), Show Mascot When Idle, Show Song Peek, prayer zone, Refresh Prayer Times, Launch at Login, Quit.
- While music plays, the notch shows white equalizer bars on the left and Kapas dancing on the right.
- When a song starts or changes, the notch grows down for 4 seconds to show the art, title, artist and time, then shrinks back (turn off: Gear → Show Song Peek). Hover it to open the full panel.
- When it's quiet, Kapas sits on the right and the next prayer time on the left (turn off: Gear → Show Mascot When Idle).

## Prayer times
- Official JAKIM times. The zone comes from your location when you're online (api.waktusolat.app), otherwise the last zone used, otherwise WLY01 (Kuala Lumpur, Putrajaya). Falls back to JAKIM e-Solat directly if that API is down. A month is cached, so it works offline.
- 10 minutes before: the notch shows "ASR 9m" and Kapas perks up. No sound.
- At prayer time: a card drops down ("Time for Asr"), Kapas sits on his sejadah, and any music pauses for 5 seconds then resumes. The card closes after 30 seconds; hover it to open the full panel.
- For 15 minutes after, the notch shows "ASR now" with Kapas on the sejadah.
- Open panel: today's five times across the top, the next one highlighted, with a countdown.
- Gear menu shows the current zone and has Refresh Prayer Times.

## Rebuilding after a code change
Double-click `build.command`. It builds, puts `NotchZero.app` in this folder, and launches it.
Or open `NotchZero.xcodeproj` in Xcode and press Cmd+R.

## Permissions (one time)
- **Calendar**: asked the first time the panel opens.
- **Location**: asked at launch, used only to pick the JAKIM prayer zone.
- **Spotify / Music**: "NotchZero wants to control…" → Allow. If you missed it: System Settings → Privacy & Security → Automation → NotchZero.
- The app is signed for this Mac only (ad-hoc), so macOS may ask again after a rebuild.

## Keeping it
Drag `NotchZero.app` into Applications, open it from there, then Gear → Launch at Login.

## Code layout
- `NotchZero/Notch/` — notch window, hover/drag logic, notch shape
- `NotchZero/Views/` — UI (music, calendar, stats, AirDrop, dot sphere, silver ring, Kapas)
- `NotchZero/Services/` — Spotify/Music (AppleScript), Calendar (EventKit), CPU/RAM/battery, AirDrop, prayer times
