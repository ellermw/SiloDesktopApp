# Coven Academy blank desktop detail page

Status: root cause reproduced and repaired in published26. This is a desktop page-lifecycle failure. Playback resolution itself continues to work. Broader visual/functional parity remains incomplete.

## Evidence

The user's installed 1.2.0 crash log at October 3, 00:33:44 records `NullReferenceException` in `ItemDetailPage.SizeTvViewport`, called by `UpdateBackdropHeight`, `UpdateUI`, and `OnNavigatedTo`. The user reported Coven Academy displays a blank series page but its Play action works; the WebUI displays the series normally.

`DetailFirstNavigationNativeFixture` uses actual WinUI `Frame.Navigate` into the published page with a completed detail prefetch, before the Frame belongs to a XamlRoot. The published25 build fails with exactly the same four-method stack (`native-first-navigation-red25.log`). No server, live credentials, or installed app control is involved.

The new TV viewport assumed `XamlRoot` existed when `ActualHeight` was zero. A completed card prefetch lets the async navigation method continue synchronously before the Frame attaches its page. `UpdateUI` calls sizing before assigning the title and other metadata, so the null dereference aborts that setup. The app's global handled-exception path keeps the page and Play action alive, producing the reported blank-page/working-playback combination. Previous TV tests attached and measured their pages before painting, which missed this lifecycle boundary.

## Repair

`SizeTvViewport` defers window-dependent layout until it has an attached XamlRoot. Metadata painting and companion data requests can finish immediately. The existing Loaded and root SizeChanged handlers then size the TV composition with the actual window dimensions. No artificial network delay, retry, or fallback media type is introduced.

## Verification

- `native-first-navigation-green26c.log`: exit0 and completion marker; series, season, episode and movie metadata paints before attachment, renders title/overview after attachment at1280/460 widths, and survives Back navigation. Actual native captures inspected. The test drains Unloaded callbacks/releases navigation history before disposing its isolated scope and collects detached wrappers while its dispatcher is alive; this avoids a separate WinRT test-host finalizer fault during CLR shutdown.
- `published-playback26.log`: six real published PlayerService transport/recovery checks pass.
- `unit-regressions26b.log`: all1386 tests pass, including real bundled-mpv OSD regression cases. Four outdated source-shape assertions were updated for the separately implemented current-server native PKCE flow, ordered ratings and title-art preference; native ordered-rating/title-art acceptance also passes.
- `native-account26.log`: native sign-in/settings/required-password controls pass.
- `native-latest-media26b.log`: current ordered ratings/title-art states and desktop rail boundary checks pass. Its effective-settings response was corrected to the official typed `items` collection; no production title-art change was needed for that fixture mismatch.
- `native-access-navigation26.log`: item/watch preparation retires old authority and active-route refresh preserves Back/Forward history.
- `parity-publish-final26d.log`: Release x64 publish succeeds. One existing nullable artwork warning remains.

## Installer checkpoint

Local installer: `D:\SiloPlayer\installer\output\SiloInstaller-1.2.0-OSD-and-Detail-Repair-Setup.exe`.

This is an interim1.2.0 candidate, superseding the OSD-only installer. Its immutable published26 payload includes the verified OSD script repair and this detail-page repair. Packaging manifest, script, installer checksum and compiler output are recorded in ignored `.codex-tmp/interim-detail-build.json` and `.codex-tmp/interim-detail-installer-oct3.log`.

No app was closed/restarted or installed by the agent. No GitHub release/main push or production infrastructure change was performed. Final1.2.N numbering and release remain tied to completion of the outstanding parity acceptance list.
