# Silo Desktop Player

A native Windows client for [Silo Server](https://github.com/Silo-Server/silo-server), built with WinUI 3 and libmpv. Browse your libraries and play media with broad codec support, hardware decoding and a preference for direct play.

## Download

[**Download version 1.2.505 for Windows x64**](https://github.com/ellermw/SiloDesktopApp/releases/download/v1.2.505/SiloInstaller-Windows-x64.exe)

Requires Windows 10 version 1809 or newer, or Windows 11, and access to a Silo Server. Runtime dependencies are included in the installer.

[Release notes](https://github.com/ellermw/SiloDesktopApp/releases/tag/v1.2.505) · [SHA-256 checksum](https://github.com/ellermw/SiloDesktopApp/releases/download/v1.2.505/SiloInstaller-Windows-x64.exe.sha256) · [All releases](https://github.com/ellermw/SiloDesktopApp/releases)

Version 1.2.505 adds centered update windows and changed-file updates that prepare the next version while Silo stays open until the final restart. It also preserves playback when selecting subtitles, corrects pending subtitle selection ownership, and improves image caching, collection editing and sign-in reliability. Existing legacy installations require one full installer migration; compatible484 or newer installations can reuse unchanged files. See the [41-change release record](docs/releases/1.2.505.md).

Historical navigation slowdown and a separate WinUI navigation crash remain under investigation.

The installer is unsigned, so Windows may display an unknown-publisher warning.

## Getting started

1. Install and launch Silo Desktop Player.
2. Enter your Silo Server address and sign in.
3. Choose your profile, open a library and start playback.

## Features

- Native playback with support for HEVC, H.264, AV1, HDR, Dolby Vision and lossless audio, subject to source and hardware compatibility.
- Home, library browsing, search, Continue Watching, Next Up and watched-state tracking.
- Profiles, Requests, Collections, personal lists, Calendar and Notifications.
- Audio-track and subtitle selection, playback quality controls, and audiobook and reader support.
- Built-in SiloCast receiver for mobile remote control, plus optional experimental AI upscaling.
- Update notifications, in-app release notes and compatible changed-file updates, with continuous progress until the final restart or optional installation when you close Silo.

SiloCast is enabled by default unless you previously disabled it. Keep the desktop app running and the phone and PC on the same Private local network. Its toggle is under **Settings → Playback → Desktop playback → Allow SiloCast control**. Physical phone compatibility testing remains ongoing.

HDR, Dolby Vision and audio output depend on the media, playback path and connected hardware. Physical Dolby Vision display output and every legacy container are not certified. The app remains in active development; complete WebUI parity is an ongoing goal.

## Documentation and support

- [Playback engine and Dolby Vision compatibility](docs/releases/1.2.309.md)
- [AI upscaling and hardware testing](docs/multigpu-upscaling-testing.md)
- [Release history](CHANGELOG.md) and [detailed release records](docs/releases)
- [Development and build documentation](docs/current-development.md)
- [Report a bug](https://github.com/ellermw/SiloDesktopApp/issues)

## License

Proprietary software. All rights reserved; use and distribution require the owner's permission. See [LICENSE](LICENSE).

Third-party components retain their respective licenses. See the [shader notices](libs/mpv/shaders/README.md) and [reader notices](src/SiloPlayer/Assets/Reader/THIRD-PARTY-NOTICES.md).
