# Duckr

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

## The window

- **Screen**: status (LISTENING, DUCKING, HOLD, FORCED, POWER OFF), the focused app and its level, and where the other apps are.
- **Meters**: FOCUS level and OTHERS volume.
- **Sliders**: duck level (0% = mute), fade down, fade up, hold, sensitivity. Changes apply at once and save themselves. The mouse wheel works on them.
- **Buttons**: POWER, FORCE (also `Ctrl+Alt+D` anywhere), ALL APPS / MUSIC ONLY, SETUP, STARTUP (start with Windows).
- **Apps list**: every app using audio with a live level. Click **M**, **N** or **F** on a row to put it in a list:
  - **M** Music apps: what gets lowered in MUSIC ONLY mode.
  - **N** Never touch: never lowered and never triggers (calls, OBS).
  - **F** Lower while focused: for DAWs on **ASIO** drivers, which skip the Windows mixer so no app can hear them. These lower everything else the whole time they're focused.

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

## License and credits

Duckr is licensed under the **GNU GPL v3** (see `LICENSE`).

Duckr started as a fork of [Background Muter](https://github.com/nefares/Background-Muter) by Nefares (GPL v3), which automatically mutes apps in the background. Thanks to that project for the starting point.
