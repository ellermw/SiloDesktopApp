namespace ContinuumPlayer.Core.Services;

public record ThumbhashImage(int Width, int Height, byte[] Rgba);

public static class ThumbhashDecoder
{
    public static ThumbhashImage Decode(string base64Hash)
    {
        var hash = Convert.FromBase64String(base64Hash);
        if (hash.Length < 5) throw new ArgumentException("Invalid thumbhash");

        int header = hash[0] | (hash[1] << 8) | (hash[2] << 16);
        int lDc = header & 63;
        int pDc = (header >> 6) & 63;
        int qDc = (header >> 12) & 63;
        bool hasAlpha = (header >> 23) != 0;
        int header2 = hash[3] | (hash[4] << 8);
        bool isLandscape = (header2 >> 12 & 1) != 0;
        int lx = Math.Max(3, isLandscape ? (hasAlpha ? 5 : 7) : (header2 >> 13) & 7);
        int ly = Math.Max(3, isLandscape ? (header2 >> 13) & 7 : (hasAlpha ? 5 : 7));

        int w = isLandscape ? 32 : (int)Math.Round(32.0 * lx / ly);
        int h = isLandscape ? (int)Math.Round(32.0 * ly / lx) : 32;
        w = Math.Max(1, w);
        h = Math.Max(1, h);

        float l = (float)lDc / 63.0f;
        float p = ((float)pDc / 31.5f - 1.0f);
        float q = ((float)qDc / 31.5f - 1.0f);

        float r = Math.Clamp(l + 0.3963f * p + 0.2158f * q, 0, 1);
        float g = Math.Clamp(l - 0.1055f * p - 0.0638f * q, 0, 1);
        float b = Math.Clamp(l - 0.0894f * p - 1.2914f * q, 0, 1);

        var rgba = new byte[w * h * 4];
        byte rb = (byte)(r * 255); byte gb = (byte)(g * 255); byte bb = (byte)(b * 255);

        for (int i = 0; i < w * h; i++)
        {
            rgba[i * 4] = rb;
            rgba[i * 4 + 1] = gb;
            rgba[i * 4 + 2] = bb;
            rgba[i * 4 + 3] = 255;
        }

        return new ThumbhashImage(w, h, rgba);
    }
}
