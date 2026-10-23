# Changelog

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
