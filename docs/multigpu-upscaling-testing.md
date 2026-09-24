# Experimental AMD / Intel upscaling test guide

This development build extends the existing NVIDIA option. It has not been
published as a stable release. Actual Intel VSR activation and AMD/Intel shader
performance still require testing on those machines.

## Choose a mode

Open Settings > Playback > Desktop playback > AI video upscaling (experimental).
Select a mode, then **restart Silo for Windows Desktop App**.

- NVIDIA RTX: select NVIDIA RTX VSR and enable Video Super Resolution in NVIDIA App.
- Intel: select Intel VSR. An Intel adapter is only a candidate; support depends on its driver/hardware. If unsupported, also try FSRCNNX AI as the portable alternative.
- AMD: select FSRCNNX AI. This is a bundled trained neural-network shader, not AMD's driver video upscaling or FSR.
- Automatic: prefers NVIDIA RTX, then AMD with FSRCNNX, then Intel VSR. Explicit selection is useful on hybrid laptops.
- Off: returns to normal playback. An old saved NVIDIA preference migrates to NVIDIA RTX VSR.

No shader download or separate AI runtime is required. FSRCNNX also runs on
NVIDIA, allowing the same shader to be tested locally before AMD testing.

## Test sequence

1. Note the GPU model, driver version, Windows version, selected mode, and monitor resolution. Check which GPU Settings says was selected, especially on hybrid laptops.
2. Play the same 1080p SDR clip fullscreen on a 4K display with the mode Off, then enabled after restarting. Use both live-action and animation; inspect fine lines, faces, compression artifacts, and motion.
3. Press I for Playback Info. Record Source video, Processed video, Upscaler, Enhancement status, and Dropped frames. GPU utilization alone does not establish AI activation.
4. NVIDIA/Intel are capped at 2x per dimension. For 720p, their processing stage can reach 1440p; the renderer then fits the monitor. Requested Intel VSR does not prove that a particular Intel driver performed AI enhancement.
5. FSRCNNX performs 2x luma reconstruction inside the renderer. Its separate requested-reconstruction row can say 3840x2160 while Processed video still says 1920x1080. Its shader activates above a 1.3x enlargement in both dimensions and uses the normal scaler to fit the display afterward.
6. Resize below the activation threshold and back to fullscreen. Seek, pause/resume, and change episodes. Check that playback and artwork remain stable.
7. Try native 4K, HDR, and a window smaller than the source. They should use normal scaling. Confirm the setting did not change the server quality selection, bitrate target, or SDR/HDR status.
8. Watch for repeated dropped frames, green/black frames, excessive GPU load, or a normal-scaling fallback message. Switch Off and restart if the mode is unsuitable on that hardware.

Send the observations and `%LOCALAPPDATA%\SiloPlayer\upscaling_log.txt` to the
desktop developer. This status log contains processor selection and dimensions;
do not share settings.json, authentication credentials, or an unredacted general
playback log. Keep any server URLs/tokens out of reports.

## Local verification

Automated tests cover vendor selection, software-adapter rejection, NVIDIA
preference migration, missing candidates, scaling/HDR exclusions, and the actual
Lua Playback Info display. A native renderer harness exercises the real
MpvPlayer with synthetic SDR video, verifies FSRCNNX GPU passes, and checks shader
removal/reinstatement on resize. This verifies the portable processing path on
the local RTX GPU; it does not simulate Intel or AMD driver behavior.

Shader source: https://github.com/igv/FSRCNN-TensorFlow/releases/tag/1.1.
Its LGPL-3.0-or-later notice, source, and license texts ship in libs/mpv/shaders.
