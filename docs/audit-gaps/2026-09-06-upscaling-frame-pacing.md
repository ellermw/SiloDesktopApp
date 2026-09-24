# RTX upscaling frame-pacing investigation

## Report and correction

The user perceives reduced motion smoothness when upscaling a 1080p movie/episode with RTX VSR, compared with the native 4K version. Playback Info reports zero drops.

The initial diagnostic interpretation was wrong. PresentMon 2.5.1 recorded some presentations with `Runtime=Other`, `SwapChainAddress=0x0`. The first analyzer excluded those records, leaving apparent 83–126 ms gaps in the identified DXGI stream and reporting roughly 22 FPS with RTX. Those numbers do not establish frame loss, or an RTX-induced increase in stuttering.

PresentMon's `PMTraceConsumer::FindOrCreatePresent` can recover presentations from kernel events when a runtime event is missing or unmatched. Such presentations initially have no identified swap chain. In these captures there is one identified video stream; the unattributed records fall into most of the apparent gaps. These records must not simply be discarded. Merging them is a diagnostic reconstruction, not proof of unique-content frame rate; arbitrary multi-stream UI captures must not be merged this way.

Source: https://github.com/GameTechDev/PresentMon/blob/main/PresentData/PresentMonTraceConsumer.cpp

## Corrected live trace interpretation

Approximately 20 seconds per sample, captured from the installed app. The user confirmed these were versions of the same title, then disabled upscaling and restarted for the last sample. Samples are not the same scene or simultaneous trials.

| Playback | Reconstructed recorded presentation FPS | Unattributed records retained | Remaining recorded intervals >70 ms |
|---|---:|---:|---:|
| 1080p, RTX requested | 23.88 | 37 | 2 |
| Native 4K | 23.77 | 13 | 4 |
| Same 1080p version, upscaling off | 23.77 | 8 | 4 |

The remaining intervals cannot be classified as actual visible freezes from this trace alone; missing/unmatched events and capture boundaries remain possible. Both RTX sample intervals occur close to the beginning/end of the capture. The earlier 38/17/12 gap counts, 68% reduction, and conclusion that RTX caused extra pauses are withdrawn.

## Controlled local tests

Probe: `.codex-tmp/frame-pacing-diagnostic/probe/FramePacingProbe.csproj`. Uses the real `MpvPlayer.InitializeWithWindow` and bundled libmpv v0.41.0-243-g05fac7f21, including `gpu-next`, D3D11, `video-sync=audio`, and `framedrop=vo`. A separate test window leaves the installed app untouched. User paused the movie to avoid concurrent playback load.

Input: local generated moving test pattern, 1920x1080 SDR at 24000/1001 FPS, plus a local silent PCM audio track providing the audio clock. Each stage settles for three seconds, then runs for 15 seconds. Same initialized player and source; only the labeled `d3d11vpp=scale=2:scaling-mode=nvidia` filter changes. The automatic filter manager is disabled inside this isolated probe to permit explicit on/off control. RTX stages report 3840x2160 D3D11/NV12 video output.

First run used the 3440x1440 ~175 Hz monitor and showed ~24 FPS with no >70 ms gaps in all four stages. It does not represent the user's 4K display, so a second run enabled per-monitor DPI awareness and selected the actual 3840x2160 display (reported refresh 59.892 Hz).

| 4K display stage | Presentation FPS | Longest presentation interval |
|---|---:|---:|
| Upscaling off, first | 23.979 | 43.67 ms |
| RTX, first | 23.977 | 44.11 ms |
| Upscaling off, second | 23.976 | 44.02 ms |
| RTX, second | 23.976 | 44.01 ms |

No stage has an interval >70 ms. mpv time-position notifications also have no >70 ms interval during these measured stages. Notification callbacks are not a direct scanout measurement. One cumulative drop is recorded after the first filter insertion; that includes transition/warmup and is not evidence of ongoing frame loss. The second RTX stage records zero.

Limitations: generated video exercises the GPU upload and VSR path but does not reproduce compressed H.264/HEVC hardware decoding, server delivery, the full WinUI host, or the exact movie scene. Presentation timing does not detect repeated image content or all perceptual effects of AI processing. No root cause for the user's visual difference has been established. No player behavior fix has been applied or verified.

## What is normal

At 23.976 FPS, a new source frame arrives about every 41.7 ms. On an approximately 60 Hz display, a non-interpolated 24 FPS movie commonly alternates roughly 33 and 50 ms display holds because 60/24 is 2.5 refreshes per frame. That cadence can create judder. Recurrent actual 83–125 ms holds are not required by ordinary 24-on-60 cadence. The current measurements do not prove such holds are happening in the user's playback.

mpv's `frame-drop-count` and `decoder-frame-drop-count` measure specific player drops, not all Windows presentation delays. The OSC's Corrupted frames row is hard-coded to zero and cannot establish decode integrity.

Reference: https://mpv.io/manual/stable/#property-list

## Artifacts and follow-up

Raw CSVs, corrected analyzer, probe, and telemetry are preserved under `.codex-tmp/frame-pacing-diagnostic/`. No credentials, live media URLs, or credential JSON were captured. The signed PresentMon executable came from the official GameTechDev release.

Next useful reproduction, if the visual difference persists: compare the same scene of the same encoded 1080p file with the filter on/off, recording frame timestamps and output images to distinguish uneven scheduling from repeated/smoothed image content. Preserve source FPS and display refresh and avoid using the previous invalid gap counts as the pass/fail criterion.
