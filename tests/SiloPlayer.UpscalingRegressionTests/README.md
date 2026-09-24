# Real GPU FSRCNNX regression

Run on Windows with a physical NVIDIA, AMD, or Intel Direct3D GPU:

```powershell
dotnet run --project tests/SiloPlayer.UpscalingRegressionTests -c Release
dotnet run --project tests/SiloPlayer.UpscalingRegressionTests -c Release -- --1080p
```

This standalone host references the real MpvPlayer and bundled libmpv. It creates
an off-screen native test window and synthetic YUV SDR video, then verifies:

- The FSRCNNX shader is loaded and its aggregation pass actually ran in vo-passes.
- Shrinking to native size removes the shader.
- Enlarging the window reinstates the shader.
- Missing shader assets report the fallback in Playback Info while playback continues.

It uses no Silo server, saved credentials, or real media. Generated video, pass
timings, and mpv logs stay under ignored bin output. The host sets the process-local
SILOPLAYER_LOG_DIRECTORY override to avoid modifying normal app logs.
A passing run validates this GPU/driver only; Intel VSR
is a separate driver path and needs separate physical-hardware validation.
