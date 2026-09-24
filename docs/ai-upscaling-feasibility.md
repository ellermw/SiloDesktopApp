# AI upscaling feasibility

**Development update, 2026-09-05:** the experimental build now adds Intel VSR
and portable FSRCNNX neural upscaling for AMD/NVIDIA/Intel. See
[the current multi-GPU test guide](multigpu-upscaling-testing.md). The NVIDIA-only
preview described below is the original 1.1.100 implementation, retained as
historical research and verification context.

Research date: 2026-09-04. Scope: local 1080p-to-4K playback enhancement. Initial findings below are followed by the implemented preview and its validation results.

## Desktop preview

The local preview adds **Settings → Playback → Desktop playback → NVIDIA RTX AI upscaling (preview)**. It is off by default and stored only on this PC. Restart Silo after changing the setting; the renderer's GPU is selected when it is created. Enable Video Super Resolution in NVIDIA App → System → Video as well. Its quality settings are controlled by NVIDIA.

The preview selects an RTX candidate through DXGI, including discrete adapters on hybrid systems. It requests mpv's NVIDIA D3D11 processing only for eligible SDR input at or below 1920×1080 when the viewport is larger, preserving aspect ratio and limiting enlargement to 2×. Resizes settle before filters change. Native 4K, HDR, unknown transfers, rotated video, and anamorphic video bypass enhancement. This conservative HDR bypass is an implementation limit of the preview, not a claim that NVIDIA cannot upscale HDR.

Playback Info (`I`) separates **Display area**, **Source video**, and **Processed video**. Processed dimensions come from mpv's live `video-out-params` after video filters, never from fullscreen/window size. **Scale per dimension** and **Pixel count** compare these frames with the source (2× each dimension means 4× pixels); **Upscaler** identifies the requested NVIDIA mode from the live filter list. The NVIDIA indicator remains the confirmation of driver-level AI activation.

Processing warnings/errors remove the enhancement for that file. The setting never changes server quality requests or creates new media files. Logs in `%LOCALAPPDATA%\SiloPlayer\upscaling_log.txt` distinguish requested processing from normal scaling; requested processing and a 4K output do not prove driver-level AI activation.

Validation on this PC's RTX 5080:

- Release x64 publish succeeded; 863 automated tests passed, including 26 upscaling cases.
- A hidden native smoke harness used the actual `MpvPlayer` wrapper and bundled libmpv with a synthetic 1080p/24 fps source. Output became 3840×2160; mpv logged NVIDIA extension enablement. Sampled decoder and renderer dropped-frame counters were zero.
- Verified pause preservation, removing the filter when shrinking the viewport, consecutive 1080p → 4K/HDR loads and accurate status, and enhancement on a later eligible file.
- Injected a native-shaped warning event for video processor input-view failure. Verified filter removal, no retry on the same file, and preserved playing state.
- Playback Info follow-up: 867 tests passed. Four native Lua runtime cases verify the actual rendered stats for RTX 1080p → 4K, unprocessed 1080p in a 4K window, native 4K, and cropped widescreen content.

These are short functional checks, not a sustained performance or image-quality benchmark. Visible movie comparisons, driver activation indication, 60 fps, grain/motion detail, hardware-decoded HEVC, subtitles, multi-monitor behavior, and other GPU/driver combinations still need hands-on validation. Automatic fallback for sustained high GPU load is not implemented; use NVIDIA's automatic quality selection or turn the preview off if playback stutters.

Local verification payload: `D:\SiloPlayer\publish-test\rtx-bitrate-final\SiloPlayer.exe`. This includes the upscaling preview, restart wording, quality-selection fix, and bitrate display described below. The user subsequently authorized publishing the combined 1.1.100 build and installer to GitHub main; current release details are maintained in README.md and CHANGELOG.md.

## Stream bitrate display

Playback Stream Info now separates **Target video bitrate** (the effective server encoder recipe in kbps) from **Video bitrate (measured)** and **Audio bitrate (measured)**. mpv measures compressed packets in bits per second, before AI processing; these media rates exclude download-buffering behavior and should not be labeled network speed. Direct/remux delivery has no encoder target. Missing measurements read Unavailable, and missing source track bitrates read Not supplied. Original-file bitrate remains in Current Source File.

Reference: [mpv bitrate property documentation](https://mpv.io/manual/stable/#command-interface-video-bitrate). Five native Lua runtime fixtures cover 720p/480p targets, source separation, no-target playback, unavailable measurements, and replacement of a previous target. Server-response regression tests verify recipe forwarding, including source-limited targets. Full suite: 888 passing tests. NVIDIA RTX eligibility and the 2× scale cap are unchanged.

Local install: rebuilt the 1.1.100 installer in `installer/output/rtx-bitrate` and updated `C:\Program Files\Silo Desktop Player` successfully (installer exit 0). No GitHub release or source push was performed.

GitHub release preparation: source commit `e1ba550308985c490ec738c0e234d2f24edba569`, 888 x64 Release tests passing, clean installer/build.ps1 packaging, bundled libmpv hash/create validation, and responsive-window startup/normal-close smoke test. Final payload is `.codex-tmp/release-1.1.100`; installer SHA-256 is `9D62A30613B3627CEBB0022C0A8644D4BB91323ECA5CC08E4D71271051A3AA6F`. Source changes since that commit are release documentation only.

## Quality selection follow-up

The desktop normalized compound menu choices such as `720p-medium` to `auto`, so the server could return original 1080p while the menu continued to show the requested 720p tier. Preserve all nine explicit resolution/bitrate tiers in both initial playback and replans, including subsequent audio changes. Reflect the returned plan in the quality menu and validate both resolution and bitrate, accounting for source-limited bitrate and cropped video. Original delivery reports Original even if a lower tier was requested.

Protocol reference: official `https://github.com/Silo-Server/silo-server` main fetched directly at commit `658be10eb03615f104790fba0431a4d19fd02d15`. Reviewed quality normalization, available ladder tiers, and source-bounded recipes. No production state changed.

Validation: the original defect failed all ten initial quality regression cases before the fix. Fifteen quality tests now pass, covering all nine tiers, initial selection, selection persistence through audio replans, source limits, mismatched recipes, and original fallback. Full suite: 883 passing tests. Live low-bandwidth playback still needs user verification. The local AI preview remains capped at 2× per dimension: a 1280×720 stream produces up to 2560×1440 filtered frames, followed by normal rendering to a 4K display.

## Recommended first experiment

Use mpv's existing `d3d11vpp` filter before considering custom SDK integration. The current stable manual documents `scale=2:scaling-mode=nvidia`, requiring a D3D11 context and enabling NVIDIA RTX Super Resolution. Hardware and driver settings determine whether enhancement actually operates. It also documents Intel VSR as another mode. [mpv manual](https://mpv.io/manual/stable/#video-filters-d3d11vpp)

Candidate filter for a 1920x1080 input:

```text
vf=d3d11vpp=scale=2:scaling-mode=nvidia
```

This belongs after decoding, before display. Architectural implication: the server can still direct-play the original 1080p file; only the client GPU produces larger frames. No new server encode or stored 4K copy is required. This is an integration recommendation, not a verified result in the bundled player.

The current upstream implementation sets the NVIDIA processor extension and logs either enablement or failure. Failure only warns, so a loaded filter or 3840x2160 output alone cannot establish that AI enhancement is active. Confirm the packaged mpv supports the filter, select the RTX adapter on hybrid systems, verify driver activation, and compare playback with enhancement disabled. [mpv implementation](https://github.com/mpv-player/mpv/blob/master/video/filter/vf_d3d11vpp.c)

## Capability and limits

NVIDIA describes RTX Video Super Resolution as trained AI enhancement of decoded frames, including edge/detail reconstruction and compression-artifact reduction, with real-time media playback as an intended use. This is a real shipping capability, but the reconstructed details should not be represented as original native-4K detail. [RTX Video SDK](https://developer.nvidia.com/rtx-video-sdk)

NVIDIA's current SDK requirements list Windows 10 x64 or newer and GeForce RTX 20-series or newer; version 1.1 adds RTX 50-series support, CUDA, improved models, and 10-bit super resolution. Supported APIs include DX11, DX12, Vulkan, and CUDA. A direct SDK integration is therefore a fallback for control or functionality the existing filter cannot supply, rather than the first development step. [SDK getting started](https://developer.nvidia.com/rtx-video-sdk/getting-started)

Do not repeat old claims that VSR only supports SDR: NVIDIA says HDR video upscaling arrived in January 2025. Its FAQ also warns that fixed quality levels exceeding available GPU performance can stutter. Keep native HDR upscaling distinct from optional SDR-to-HDR conversion. [NVIDIA FAQ](https://nvidia.custhelp.com/app/answers/detail/a_id/5448/~/rtx-video-super-resolution-faq)

For the prototype, leave `nvidia-true-hdr` off. That separate mpv option changes SDR to HDR; current source estimates a 1000-nit output maximum and warns that this is a guess requiring adjustment to match NVIDIA settings. Validate native 10-bit/HDR preservation separately. [mpv implementation](https://github.com/mpv-player/mpv/blob/master/video/filter/vf_d3d11vpp.c)

## Acceptance checks proposed

- Start with 1080p SDR film at 24 fps; also test 60 fps, low-bitrate footage, grain, faces, text, and motion. Inspect moving sequences, not only paused screenshots.
- Compare normal scaling with VSR at matched display size. Record dropped frames, GPU use, seek/start delay, and color consistency.
- Upscale only when the display area benefits. Preserve aspect ratio for cropped films; do not blindly double native 4K content.
- Test subtitles, HDR10, existing Dolby Vision handling, fullscreen/window transitions, multiple displays, and unsupported GPUs. These are unresolved integration checks.
- Offer opt-in enhancement with normal scaling fallback; do not advertise AI as active solely because filter initialization succeeded.

## Other approaches

FSRCNNX is a trained convolutional network distributed as an mpv GLSL shader, not merely a conventional sharpening filter. It provides a potential broader-GPU option, but suitability and real-time performance need comparison on the actual hardware. The maintainer notes that its special LineArt model can produce an oil-painting appearance on natural content, so use the general model for film experiments. [FSRCNNX releases](https://github.com/igv/FSRCNN-TensorFlow/releases)

Offline enhancement is a separate product direction: Topaz Video provides model selection, previews, and file export. Precomputing alternate 4K versions removes the player's real-time processing deadline but requires processing and storage. It is not evidence that those models can run live inside this player. [Topaz workflow](https://www.topazlabs.com/learn/topaz-video-upscaling-basics)

No production server parity comparison was needed for this local rendering feasibility question.
