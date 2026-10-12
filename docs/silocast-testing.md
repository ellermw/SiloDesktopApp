# SiloCast testing — Windows 1.2.243

This local test build adds a SiloCast receiver to Silo for Windows. It implements the current official Silo phone protocol and uses native libmpv playback. Actual iPhone/Android interoperability still needs device testing.

## Set up

1. Install `SiloInstaller-1.2.243-SiloCast-Test-Setup.exe`. Select **Allow SiloCast phone control on private local networks** in the installer.
2. Sign into the desktop app. Under **Settings > Playback > Desktop playback**, enable **Allow SiloCast control**. Its status should say **Ready**.
3. Keep the desktop app running, including when minimized. Windows must mark your Ethernet/Wi-Fi network **Private**. Keep the phone and PC on the same local network.
4. In the current Silo iPhone or Android app, open its Remote/cast controls and select this PC by its Windows machine name. On iPhone, allow Silo local-network access if prompted.
5. Choose a movie or episode on the phone and launch it on this PC. The phone approves a temporary playback profile through the Silo server. The desktop opens playback in fullscreen after the video surface is ready.

## Test

- Discovery and initial launch; resume a partly watched item and start another from the beginning.
- Pause/resume, seek, volume/mute, audio tracks, subtitles/off, subtitle delay/position, quality, aspect and speed.
- Next episode and automatic next episode; confirm progress belongs to the phone's selected profile.
- Disconnect the phone: media should continue. Reconnect and resume control. Let the app minimize and verify controls still work.
- Start playback from the desktop while a remote movie plays: the desktop should take over using its own profile.
- Stop remote playback, use **Disconnect phone** in desktop settings, disable SiloCast, and close the desktop app. Confirm the server session stops and saved desktop login/profile stay intact.
- Change phone profiles while preparing playback; canceled/denied handoffs should leave existing playback untouched.

If the PC is missing, check Windows network category, installer firewall option, iPhone local-network permission, VPNs, and guest Wi-Fi/client isolation. The receiver excludes Public/VPN/virtual interfaces. It cannot wake the PC or start a closed app.

Report the phone model/app version, Windows version, server version, chosen profiles, and the first step that fails. Receiver diagnostics are in `%LOCALAPPDATA%\SiloPlayer\silocast.txt`; playback diagnostics include `state_trace.txt` and `progress_error.txt`. Do not share `settings.json`, credentials, or tokens.

## Source baseline and verification

This build uses desktop commit `d5de162a9669418d072db99aa12e3fb66fe2bd1a` (published 1.2.241) plus every tracked and new source change from the local 1.2.242 badge-fix candidate. The official desktop GitHub main was fetched directly on October 12 and matched that commit. SiloCast is adapted to its playback context fences and library/file selection APIs.

The earlier `SiloInstaller-1.1.106-SiloCast-Test-Setup.exe` used a stale 1.1.105 source baseline and is obsolete. Do not install it. Version 1.2.243 is a rebuilt payload from the current source, not a relabeled old installer.

The corrected x64 payload publishes successfully and Inno Setup compiles the installer. Automated checks pass: 1,657 main tests, 14 receiver tests, 16 discovery tests (including native private-interface advertisement), 23 published playback checks, and 42 rendered badge cases preserving the 1.2.242 fix. The actual settings card was checked at 600- and 900-pixel widths. Real-phone discovery and playback remain for device testing.

The broader default native regression run is incomplete. After repairing obsolete account/search fixture setup, it passes Home artwork, calendar, SiloCast settings, account password/history import, reader interoperability, Watch Party, appearance/controls, request interactions and personal lists. It stops in the unchanged `DetailPresentationNativeFixture` at line 55: the test expects an unavailable series action to be hidden, while the current 1.2.242 app deliberately shows a disabled action. The relevant production page/view-model and this fixture are byte-identical to the 1.2.242 baseline. This visibility assertion does not demonstrate a stale-episode playback regression. Later default native cases have not been verified in this run. The original failure and final run are retained; no assertion was skipped to claim a complete pass.

Installer: `D:\SiloPlayer\installer\output\SiloInstaller-1.2.243-SiloCast-Test-Setup.exe`, 172,135,207 bytes. SHA256: `16D2841FA4E65B31847715EC835F0CB9C8A2DFB9C66E6D4D338C701015D88C7B`. The installer payload is version `1.2.243.0`. Its 561 packaged files and all 891 frozen build inputs remained unchanged after verification. Publish reports two nullable warnings; compilation succeeds.

The owner installed this candidate and authorized GitHub publication on October 12. The complete 1.2.243 source is committed on main and tagged `v1.2.243`; [the GitHub installer](https://github.com/ellermw/SiloDesktopApp/releases/download/v1.2.243/SiloInstaller-Windows-x64.exe) contains the same verified bytes. Following cleanup, `D:\SiloPlayer` is the canonical development checkout; the attached current SiloCast worktree retains the original verification evidence. See [the current development handoff](current-development.md). No production server changes were made. This build contains SiloCast receiver support; TV Mode and directional library browsing are separate features.

The installer uses the existing local QA signing policy. It will not be installed over your running app automatically.
