# Changelog

## 1.3.0 — 2026-10-23
- **Dark / light mode for EVERY window**: the theme colours now live at application level (before, only the main window had them and the other windows kept a copy or the system look, so text could be unreadable after a mode change). The audio, help, statistics, subtitle-picker and prompt windows follow the theme live, and the title bars / borders follow it too (Windows 11 22H2+; older Windows only get the dark/light caption).
- **Moved from .NET 8 to .NET 10** (the app, the unit tests, the build scripts). LibVLCSharp.WPF 3.8.5 and libvlc 3.0.21 run unchanged on .NET 10.
- **Subtitle download**: candidates are ranked by season/episode, year, resolution, source, codec and release group (a different episode is rejected), the 3 best are downloaded and compared with the video length, transient network errors (429/5xx/timeouts) are retried, OpenSubtitles tokens are renewed, `.vtt`/`.ass`/`.ssa` and UTF-16/Greek encodings are converted to UTF-8 `.srt`.
- **Subtitle sync**: two audio segments (start and 70 %) give both the shift and the speed drift (25 ↔ 23.976 fps or any ratio within ±6 %), 3 refinement steps down to 20 ms, ±5 min search, confidence checks so a doubtful result is never applied, output through a real SRT writer.
- `Z` / `X` subtitle delay keys and "reset delay".
- New pure-logic folder `SubCore` with 39 unit tests, and an end-to-end sync check in the self-test (97 commands passed, 0 failed).

## 1.2.0 — 2026-10-23
- Renamed to **Opsis Media Player Pro** (the executable is now `OpsisMediaPlayerPro.exe`; your settings in `%AppData%\MediaPlayerPro` are kept).
- The light / dark / auto mode is no longer in the menu: it is a button on the control bar (and `Ctrl+Shift+L`) that cycles dark → light → auto.
- Snapshot to the clipboard (`Ctrl+C`), move playlist items (`Alt+↑/↓`), mouse side buttons = previous / next file, window position and size are remembered.
- **Self-test**: `OpsisMediaPlayerPro.exe --selftest <file>` runs every button and menu item against a real playback; plus a static audit of the menu tags (`tests/audit-menus.py`).
- Audio files no longer trigger an online subtitle search.
- Repository, installer (Inno Setup 7, no admin, optional per-user "Open with" entry) and portable zip.

## 1.1.0
- Rounded menus with shadow, accent hover, check marks and aligned shortcuts.
- Output-device and audio-channel selection (stereo, reversed, left, right, Dolby), video track, titles and chapters menus, repeat file, stop after current file, playback statistics (`Ctrl+J`), remaining time (`T`), fit window to video (`Ctrl+1/2/5`), open URL dialog, playlist context menu, `V`/`B` cycling, `Home`/`End`, jump keys with Alt/Shift, hidden cursor in full screen.

## 1.0.7 and earlier
- Audio enhancer (10-band equalizer, effects, presets), side panel with movie/series information, themes with light/dark, subtitle download and sync (Podnapisi, OpenSubtitles.com, Subs4Free), bookmarks, conversion and A-B clip export, resume position, sleep timer, Chromecast/DLNA.
