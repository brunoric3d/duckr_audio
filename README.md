# Duckr

[Download the latest installer](https://github.com/brunoric3d/duckr_audio/releases/latest)

**Your music steps back when your work plays.**

Duckr is a small Windows tray app for people who edit video or make music while listening to music. When the app you're working in starts playing sound (Premiere, After Effects, Resolve, a DAW, a video player), every other app fades down. When playback stops, they fade back up. No more reaching for the volume every time you hit play.

## How it decides

- It reads each app's live sound level from the Windows mixer.
- Only sound that **starts while an app is focused** counts. Clicking into an app that is already playing (your music player, a browser with music) does nothing.
- Sound must last a moment (120 ms) before ducking starts, so clicks and UI sounds don't pump your music.
- After playback stops it waits a bit (1.5 s by default) before restoring, so scrubbing and replaying a cut keep the music down. Switching to another app restores right away.
- The app you're focused on is never lowered.
- Volumes always come back: on exit, when you turn Duckr off, and after a crash (a safety file in `%APPDATA%\Duckr` restores them on the next start).
- Works across all output devices (audio interface, headphones, speakers).

## Quick setup

The first time Duckr starts (right after installing), a one-page Quick setup asks what to lower (all other apps or music apps only), how low (mute, 15%, 35%), whether voice calls lower the music, and whether to start with Windows. START DUCKR saves and opens the main window. Reopen it any time from the tray: **Quick setup...**

## The window

- **Screen**: status (LISTENING, DUCKING, HOLD, FORCED, POWER OFF), the focused app and its level, and where the other apps are.
- **Meters**: FOCUS level and OTHERS volume.
- **Sliders**: duck level (0% = mute), fade down, fade up, hold, sensitivity. Changes apply at once and save themselves. The mouse wheel works on them.
- **Buttons**: POWER, FORCE (also `Ctrl+Alt+D` anywhere), ALL APPS / MUSIC ONLY, SETUP, VOICE CALL (calls lower the music, see below). Start with Windows is in the tray menu.
- **Apps list**: every app using audio with a live level. Click **M**, **N** or **F** on a row to put it in a list:
  - **M** Music apps: what gets lowered in MUSIC ONLY mode.
  - **N** Never touch: never lowered and never triggers (calls, OBS).
  - **F** Lower while focused: for DAWs on **ASIO** drivers, which skip the Windows mixer so no app can hear them. These lower everything else the whole time they're focused.

## Setup

Same skin as the main window. Three tabs (MUSIC APPS, NEVER TOUCH, WHILE FOCUSED), each a list of rows:

- Click a name to edit it, or **+ add app** to add one. Typing searches the apps running right now (ones playing sound first); pick with Up/Down + Enter or a click. End a name with `*` to match anything that starts with it.
- Each row shows if that app is PLAYING, has AUDIO, is RUNNING, or isn't open. **x** removes it.
- **VOICE** (NEVER TOUCH tab, per app): when that app plays sound, your music lowers even if the app isn't focused. Made for calls: a friend talks on Discord and the music steps back. Works only while the **VOICE CALL** button (main window or tray) is on; it starts off. VOICE is preset for Discord, Teams, Zoom and Slack. Short pings are ignored (same "must play for" time).
- **Force hotkey**: click the screen and press the keys you want. Backspace sets none.
- **Must play for**: how long sound must last before lowering starts.

Changes apply at once and save themselves.

Closing the window keeps Duckr in the tray. Use the tray menu to exit.

## Known limits

- One browser is one app. Music in one Chrome tab and a reference video in another Chrome tab can't be told apart.
- ASIO audio can't be measured (use the F list).
- Windows only for now.

## Build

Needs the .NET 10 SDK. Double-click `build.bat`, or run:

```
dotnet build Duckr.sln -c Release
dotnet test tests\Duckr.Tests\Duckr.Tests.csproj -c Release
```

The app ends up in `src\Duckr\bin\Release\net10.0-windows\Duckr.exe`.

A debug log (`duckr-debug.log`) is written next to the exe.

## Installer

Double-click `make-installer.bat`. It installs the Velopack packaging tool (`vpk`), builds Duckr and makes `releases\Duckr-win-Setup.exe`. Progress goes to `installer-log.txt`.

The installer is small (.NET is not bundled): if the .NET 10 Desktop Runtime is missing, it downloads and installs it first. It is one click: it shows the Duckr splash, installs per user to `%LocalAppData%\Duckr` (no admin needed), adds Start menu and desktop shortcuts and opens Duckr. Uninstall from Windows Settings > Apps; that also removes "start with Windows".

Every local run gets a newer dev version (`0.1.0-dev.<minutes>`), so running the new Setup.exe over an installed Duckr updates it: when it asks to overwrite, answer **Yes**. For a release version, pass it: `make-installer.bat 0.2.0`.

## Updates

The installed Duckr checks GitHub releases 15 seconds after it starts and shows a tray balloon when a new version is out. Right-click the tray icon > **Update to x.y.z** (or **Check for updates**) to download it; Duckr puts all volumes back, installs the update and restarts. Copies run from a build folder don't update.

### Releases on GitHub

Push a version tag and GitHub Actions builds, tests, makes the installer and publishes a release with `Duckr-win-Setup.exe` attached:

```
git tag v0.1.0
git push origin v0.1.0
```

## License and credits

Duckr is licensed under the **GNU GPL v3** (see `LICENSE`). The installer uses [Velopack](https://github.com/velopack/velopack) (MIT).

Duckr started as a fork of [Background Muter](https://github.com/nefares/Background-Muter) by Nefares (GPL v3), which automatically mutes apps in the background. Thanks to that project for the starting point.
