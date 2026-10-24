# Dolby Vision engine verification

## Baseline and actual cause

Public304 and the preserved local305 candidate shipped `mpv v0.41.0-243-g05fac7f21`, FFmpeg `N-123079-ge869426a8`, library SHA256 `5825FFD2BEF6FD6A4B78016D5890C6C9DEDB16A31828A1C749BE89EACF9E20AE`. Source capabilities declared profiles5/8 and explicitly excluded7. A real D3D11/gpu-next run against FFmpeg's P7/hvcE test file logged **Dolby Vision enhancement-layer playback is not supported**. The rendered pass list lacked both enhancement reconstruction and the candidate's P7 reshaping. A catalog DV label or successful HEVC playback did not establish full P7 processing.

## Engine provenance

mpv's [official installation page](https://mpv.io/installation/) links the shinchiro Windows builds. The selected existing release is [20261021](https://github.com/shinchiro/mpv-winbuild-cmake/releases/tag/20261021), build repository tag commit `1f8d2e76255abc48f0456d9be99b4cab9c6566e6`. The architecture is ordinary x86_64 rather than x86_64-v3, retaining the previous CPU baseline.

- Archive `mpv-dev-x86_64-20261021-git-e471496c56.7z`, SHA256 `EFC719EFF3DC5954B4A5EFC800632808BCEC01CC9034940DD14E5EB7FE9EA633`, checked against GitHub's release-asset digest before extraction.
- Library SHA256 `04E4D27DC385DC18DDD93FBE69C75E635B864F3C6EC53A9122DF54C2F7842F4A`.
- Actual client API reports `mpv v0.41.0-1227-ge471496c5`, FFmpeg `N-127893-g9a861e968` and libplacebo `v7.377.0 (v7.360.0-176-g285d1cc-dirty)` (API377). The dependency's dirty suffix is the provider's actual build string; do not replace it with a guessed clean source version.
- mpv source commit `e471496c56f0f49c133bd88b50aed62b845f1909`. Upstream [P7 FEL implementation](https://github.com/mpv-player/mpv/pull/17932) is merged; matching FFmpeg/libplacebo functionality is required, so an mpv version string alone is insufficient evidence.

The pin is `libs/mpv/engine.json`; the supported installer build checks the source library against that pin and verifies the published copy. The original library and all previous payloads remain preserved. All37 advertised video codecs and49 advertised audio codecs were compared against the candidate's actual decoder-list with no missing entries.

## Samples and independent controls

Provenance, sizes and local hashes are under `.codex-tmp/dovi-oct24/fixture-manifest.json`. Public technical samples remain in ignored test evidence and are not packaged in the installer.

- [FFmpeg FATE P7/hvcE MKV](https://fate-suite.ffmpeg.org/mkv/dovi-p7-hvce.mkv):3840x2160, P7.6, base+enhancement+RPU. `dovi_tool` independently reports P7 FEL/CMv2.9. FFmpeg's corresponding [MP4 fixture](https://fate-suite.ffmpeg.org/mov/dovi-p7-hvce.mp4) exercises a second container.
- The one-frame FATE MKV was stream-loop remuxed without re-encoding into a10-second, approximately127Mbps fixture for actual native direct-reader seek/resume checks. Its P7 configuration and hvcE side data remain present.
- A controlled P7 MEL fixture uses dovi_tool's supplied259-frame `regular_rpu_mel.bin` (independently identified as P7 MEL/CMv4.0) injected into its matching HEVC base/enhancement test sequence, then muxed with official MKVToolNix103.0. FFmpeg independently identifies P7.1, base+EL+RPU and hvcE side data. The candidate performs reshaping without a FEL residual pass, as expected for MEL. This is a constructed developer fixture, not an unrelated downloaded film clip; source/muxer hashes and commands are retained in the ignored evidence.
- [dovi_tool regular.mkv](https://github.com/quietvoid/dovi_tool/tree/main/assets/hevc_tests) at the exact commit recorded in `sample-upstream-commit.txt`: P8.1, CMv4.0. Additional [FATE P8.1 MP4](https://fate-suite.ffmpeg.org/mov/dovi-p81.mp4) was rendered too.
- Dolby's [official P8.4 example](https://optiview.dolby.com/docs/theoplayer/examples/streaming/dolby-vision/) supplies an HLG-compatible60fps source, downloaded from its advertised clear media URL. FFmpeg independently identifies profile8, compatibility4, HLG input. The actual DV decode/reshape path reports the transformed PQ working space; this is not evidence that the source was originally PQ.
- Existing P5 support was checked with [FATE P5](https://fate-suite.ffmpeg.org/mov/dovi-p5.mp4); the candidate performs reshaping.

The P7 candidate executes a GPU pass explicitly containing **enhancement layer**. Running the identical engine/sample with `vf=format=enhancement-layer=no` removes that pass and changes the rendered frame hash. This positive/negative control establishes reconstruction rather than merely reading the P7 label. Both P8.1 and P8.4 execute reshaping. Explicit PQ/BT.2020 HDR-target probes also retain the expected passes and report PQ/BT.2020/rgb10a2; those probes do not change Windows HDR settings.

## Actual desktop integration and diagnostics

`DolbyVisionOct24NativeFixture` constructs the packaged production `MpvPlayer`, uses its real gpu-next/D3D11 configuration and native signed-direct reader against an isolated range-capable HTTP server, and checks four P7/P8 samples. It checks actual profile, decoder, reshaping/enhancement passes, pause, and seek/resume where duration permits. The first309 packaged run passes all four, including hardware d3d11va, P7 FEL in MKV/MP4, P8.1 and P8.4. The final corrected UI payload is rerun and identified by the final receipt; never combine different app hashes into a purported exact-payload result.

`GetVideoPipelineSnapshot` records engine/dependency versions, selected source profile, renderer/decoder, working and target transfer/primaries/pixel format, and actual reshaping/enhancement classifications on playback restart. It does not read/log filenames, track titles, URLs, headers or credentials. `vo-passes` requires a native node read: unlike track-list, nested scalar paths return unavailable. The live probe caught that before packaging verification; the implementation reads bounded native arrays/maps and frees the copied root exactly once. The native fixture verifies the actual flags.

## Regression details and limits

The initial main run found three stale source/capability assertions (updated to the intentional new contracts) and three HTTPS fixture failures. Investigation localized the HTTPS failures to the fixture's final legacy native load, not the signed reader/recovery stages: the new curl transport correctly rejects the untrusted self-signed certificate. The test now gives only that isolated native client the fixture's public CA PEM and adds localhost/IP SANs. No OS trust changes or production TLS-verification bypass were added. Actual public HTTPS playback of Dolby's sample succeeds with normal native verification. The focused49-case rerun and the subsequent full2,132-test suite pass.

Important boundaries:

- The tested offscreen Windows output was SDR gamma2.2/BT.709/rgb10a2; a separate explicit HDR-target probe verifies PQ mapping. This is **not** a certified Dolby Vision HDMI/display transport test. Source decoding/reshaping and display transport are different claims.
- The old FATE `dovi-p7.mp4` stores base and enhancement as separate video tracks. Current upstream renders its base layer without associating the enhancement track; it is not equivalent to the passing interleaved hvcE MKV/MP4 cases. Do not advertise universal support for every legacy dual-track container or conceal this fallback as full DV.
- P7 MEL is covered by the controlled developer fixture above; the independent FATE source reconstruction case is FEL. P5 and P8 single-layer paths were regression-checked.
- These checks do not certify every GPU/driver/display, physical HDR calibration, sustained playback of an entire film, or resolution of the historical buffering reports.

Final execution: the font-verified309 payload passes all six actual-player signed-direct cases: P7 FEL MKV/MP4, controlled P7 MEL, P8.1, official P8.4 and P5. Exact app DLL9960E2CE... and pinned engine04E4D27D... are recorded in the final309 receipt, together with nine native groups,2,132 main tests and24 published-service groups. Final files:572; source/test/build inputs:1,468. No source drift after packaging.
