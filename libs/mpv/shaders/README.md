# FSRCNNX neural upscaler

`FSRCNNX_x2_8-0-4-1.glsl` is the unmodified, trained 2x luma upscaler by igv,
from https://github.com/igv/FSRCNN-TensorFlow/releases/tag/1.1.

SHA-256: `E800DBC5C1C95185CC82216C597724533FF5F2880179F256EEF600F03E8DC2AE`.

The shader's own header licenses it under **LGPL-3.0-or-later**. `COPYING.LESSER`
and `COPYING` contain the applicable LGPL/GPL texts. This third-party asset is
not covered by the desktop application's proprietary license. Its complete
shader source and trained weights are distributed here; users may replace
this external file with a compatible modified shader, retaining its filename.

The shader reconstructs luma at 2x when both output/input dimension ratios
exceed 1.300. mpv then fits that reconstruction to the display. It is a
portable neural-network shader, not AMD's driver video enhancement or FSR.
Silo additionally limits it to eligible SDR sources up to 1080p.
