namespace SiloPlayer.Core.Services;

public record ThumbhashImage(int Width, int Height, byte[] Rgba);

public readonly record struct ThumbhashAmbientColor(byte R, byte G, byte B);

/// <summary>
/// Thumbhash decoder — ports the reference JavaScript implementation to C#.
/// https://github.com/evanw/thumbhash
///
/// Decodes a base64 thumbhash into a small (up to 32px) blurred preview image
/// that approximates the original. Produces RGBA pixel data (not BGRA — callers
/// must swap channels themselves if they need BGRA for WinUI WriteableBitmap).
/// </summary>
public static class ThumbhashDecoder
{
    /// <summary>
    /// Returns the guarded average color used by the current WebUI ambient-glow
    /// treatment. The color comes from the thumbhash DC components, so it is
    /// available without decoding the placeholder bitmap.
    /// </summary>
    public static ThumbhashAmbientColor? GetAmbientColor(string? base64Hash)
    {
        if (string.IsNullOrWhiteSpace(base64Hash))
            return null;

        try
        {
            var hash = Convert.FromBase64String(base64Hash);
            if (hash.Length < 5)
                return null;

            int header24 = hash[0] | (hash[1] << 8) | (hash[2] << 16);
            double l = (header24 & 63) / 63d;
            double p = ((header24 >> 6) & 63) / 31.5d - 1d;
            double q = ((header24 >> 12) & 63) / 31.5d - 1d;
            double b = Math.Clamp(l - (2d / 3d * p), 0d, 1d);
            double r = Math.Clamp((3d * l - b + q) / 2d, 0d, 1d);
            double g = Math.Clamp(r - q, 0d, 1d);

            int rr = (int)Math.Round(r * 255);
            int gg = (int)Math.Round(g * 255);
            int bb = (int)Math.Round(b * 255);
            double luminance = 0.2126d * r + 0.7152d * g + 0.0722d * b;

            if (luminance < 0.08d)
            {
                double boost = 0.08d / Math.Max(luminance, 0.001d);
                rr = Math.Min(255, (int)Math.Round(rr * boost));
                gg = Math.Min(255, (int)Math.Round(gg * boost));
                bb = Math.Min(255, (int)Math.Round(bb * boost));
            }
            else if (luminance > 0.85d)
            {
                double factor = 0.85d / luminance;
                rr = (int)Math.Round(rr * factor);
                gg = (int)Math.Round(gg * factor);
                bb = (int)Math.Round(bb * factor);
            }

            int max = Math.Max(rr, Math.Max(gg, bb));
            int min = Math.Min(rr, Math.Min(gg, bb));
            if (max > 30 && (max - min) / (double)max < 0.15d)
            {
                double average = (rr + gg + bb) / 3d;
                rr = Math.Clamp((int)Math.Round(average + (rr - average) * 2d), 0, 255);
                gg = Math.Clamp((int)Math.Round(average + (gg - average) * 2d), 0, 255);
                bb = Math.Clamp((int)Math.Round(average + (bb - average) * 2d), 0, 255);
            }

            return new ThumbhashAmbientColor((byte)rr, (byte)gg, (byte)bb);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    public static ThumbhashImage Decode(string base64Hash)
    {
        var hash = Convert.FromBase64String(base64Hash);
        if (hash.Length < 5) throw new ArgumentException("Invalid thumbhash");

        // Read the constants
        int header24 = hash[0] | (hash[1] << 8) | (hash[2] << 16);
        int header16 = hash[3] | (hash[4] << 8);
        float lDc = (header24 & 63) / 63f;
        float pDc = ((header24 >> 6) & 63) / 31.5f - 1f;
        float qDc = ((header24 >> 12) & 63) / 31.5f - 1f;
        float lScale = ((header24 >> 18) & 31) / 31f;
        bool hasAlpha = (header24 >> 23) != 0;
        float pScale = ((header16 >> 3) & 63) / 63f;
        float qScale = ((header16 >> 9) & 63) / 63f;
        bool isLandscape = (header16 >> 15) != 0;
        int lx = Math.Max(3, isLandscape ? (hasAlpha ? 5 : 7) : (header16 & 7));
        int ly = Math.Max(3, isLandscape ? (header16 & 7) : (hasAlpha ? 5 : 7));
        float aDc = hasAlpha ? (hash[5] & 15) / 15f : 1f;
        float aScale = hasAlpha ? (hash[5] >> 4) / 15f : 0f;

        // Read the varying factors (boost saturation by 1.25x to compensate for quantization)
        int acStart = hasAlpha ? 6 : 5;
        int acIndex = 0;

        float[] DecodeChannel(int nx, int ny, float scale)
        {
            var list = new List<float>();
            for (int cy = 0; cy < ny; cy++)
            {
                for (int cx = cy != 0 ? 0 : 1; cx * ny < nx * (ny - cy); cx++)
                {
                    int byteIdx = acStart + (acIndex >> 1);
                    if (byteIdx >= hash.Length) { list.Add(0f); acIndex++; continue; }
                    int nibble = (hash[byteIdx] >> ((acIndex & 1) << 2)) & 15;
                    acIndex++;
                    list.Add((nibble / 7.5f - 1f) * scale);
                }
            }
            return list.ToArray();
        }

        var lAc = DecodeChannel(lx, ly, lScale);
        var pAc = DecodeChannel(3, 3, pScale * 1.25f);
        var qAc = DecodeChannel(3, 3, qScale * 1.25f);
        var aAc = hasAlpha ? DecodeChannel(5, 5, aScale) : Array.Empty<float>();

        // Determine output dimensions from aspect ratio (lx/ly)
        float ratio = (float)lx / ly;
        int w = (int)Math.Round(ratio > 1 ? 32 : 32 * ratio);
        int h = (int)Math.Round(ratio > 1 ? 32 / ratio : 32);
        w = Math.Max(1, w);
        h = Math.Max(1, h);

        var rgba = new byte[w * h * 4];
        var fx = new float[Math.Max(lx, hasAlpha ? 5 : 3)];
        var fy = new float[Math.Max(ly, hasAlpha ? 5 : 3)];

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float l = lDc, p = pDc, q = qDc, a = aDc;

                // Precompute the cosine coefficients
                for (int cx = 0; cx < fx.Length; cx++)
                    fx[cx] = (float)Math.Cos(Math.PI / w * (x + 0.5) * cx);
                for (int cy = 0; cy < fy.Length; cy++)
                    fy[cy] = (float)Math.Cos(Math.PI / h * (y + 0.5) * cy);

                // Decode L (luminance)
                {
                    int j = 0;
                    for (int cy = 0; cy < ly; cy++)
                    {
                        float fy2 = fy[cy] * 2f;
                        for (int cx = cy != 0 ? 0 : 1; cx * ly < lx * (ly - cy); cx++, j++)
                        {
                            if (j < lAc.Length)
                                l += lAc[j] * fx[cx] * fy2;
                        }
                    }
                }

                // Decode P and Q (chrominance)
                {
                    int j = 0;
                    for (int cy = 0; cy < 3; cy++)
                    {
                        float fy2 = fy[cy] * 2f;
                        for (int cx = cy != 0 ? 0 : 1; cx < 3 - cy; cx++, j++)
                        {
                            float f = fx[cx] * fy2;
                            if (j < pAc.Length) p += pAc[j] * f;
                            if (j < qAc.Length) q += qAc[j] * f;
                        }
                    }
                }

                // Decode A (alpha)
                if (hasAlpha)
                {
                    int j = 0;
                    for (int cy = 0; cy < 5; cy++)
                    {
                        float fy2 = fy[cy] * 2f;
                        for (int cx = cy != 0 ? 0 : 1; cx < 5 - cy; cx++, j++)
                        {
                            if (j < aAc.Length)
                                a += aAc[j] * fx[cx] * fy2;
                        }
                    }
                }

                // Convert LPQ → RGB
                float bVal = l - 2f / 3f * p;
                float rVal = (3f * l - bVal + q) / 2f;
                float gVal = rVal - q;

                int idx = (y * w + x) * 4;
                rgba[idx] = (byte)Math.Max(0, 255 * Math.Min(1f, rVal));
                rgba[idx + 1] = (byte)Math.Max(0, 255 * Math.Min(1f, gVal));
                rgba[idx + 2] = (byte)Math.Max(0, 255 * Math.Min(1f, bVal));
                rgba[idx + 3] = (byte)Math.Max(0, 255 * Math.Min(1f, a));
            }
        }

        return new ThumbhashImage(w, h, rgba);
    }
}
