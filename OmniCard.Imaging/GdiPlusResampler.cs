namespace OmniCard.Imaging;

/// <summary>
/// Managed, platform-independent re-implementation of GDI+'s <c>InterpolationMode.HighQualityBicubic</c>
/// <c>DrawImage</c> scaling onto a transparent 32bpp ARGB bitmap — the operation the perceptual hashes were
/// originally computed with on Windows. Reproducing it (rather than using a library resampler) keeps hashes
/// computed on any OS comparable with the ones already stored in the catalogs and hash corrections.
///
/// Behaviour, reverse-engineered from GDI+ impulse/step responses and calibrated against GDI+ output:
/// <list type="bullet">
/// <item>Cubic convolution kernel with a = -1, integrated over each source pixel's unit footprint.</item>
/// <item>When shrinking, the kernel is stretched by the scale factor (prefiltering); never narrowed.</item>
/// <item>Destination pixel d samples source coordinate t = d * (src / dst) (GDI+'s default pixel offset).</item>
/// <item>Samples outside the source are transparent, so edge pixels get partial (coverage) alpha.</item>
/// <item>Separable: horizontal pass first, its premultiplied result rounded and clamped to 8 bits.</item>
/// <item>Same-size draws are plain copies.</item>
/// </list>
/// Measured against GDI+ on random and real card images: ~86% of channel values identical, the rest
/// within ±4 levels (GDI+'s internal fixed-point rounding); see OmniCard.Tests ImagingBaselineTests.
/// </summary>
internal static class GdiPlusResampler
{
    private const double A = -1.0;

    // Antiderivative (from 0) of the cubic kernel; odd, constant beyond |x| >= 2.
    private static double KernelIntegral(double x)
    {
        double sign = Math.Sign(x);
        x = Math.Abs(x);
        double v;
        if (x < 1)
        {
            v = (A + 2) * x * x * x * x / 4 - (A + 3) * x * x * x / 3 + x;
        }
        else
        {
            const double AtOne = (A + 2) / 4 - (A + 3) / 3 + 1;
            double u = Math.Min(x, 2);
            v = AtOne + Outer(u) - Outer(1);
        }
        return sign * v;

        static double Outer(double u) => A * u * u * u * u / 4 - 5 * A * u * u * u / 3 + 4 * A * u * u - 4 * A * u;
    }

    private readonly record struct Taps(int[] Start, double[][] Weights);

    // Per destination index: first in-range source index and the weights of the in-range taps.
    private static Taps BuildTaps(int srcLen, int dstLen)
    {
        double scale = (double)srcLen / dstLen;
        double k = Math.Max(1, scale);
        var start = new int[dstLen];
        var weights = new double[dstLen][];
        for (int d = 0; d < dstLen; d++)
        {
            double t = d * scale;
            int s0 = Math.Max(0, (int)Math.Floor(t - 2 * k - 0.5));
            int s1 = Math.Min(srcLen - 1, (int)Math.Ceiling(t + 2 * k + 0.5));
            var w = new double[Math.Max(0, s1 - s0 + 1)];
            for (int s = s0; s <= s1; s++)
                w[s - s0] = KernelIntegral((s + 0.5 - t) / k) - KernelIntegral((s - 0.5 - t) / k);
            start[d] = s0;
            weights[d] = w;
        }
        return new Taps(start, weights);
    }

    /// <summary>Scales a BGRA (non-premultiplied) buffer. Returns a tightly packed BGRA (non-premultiplied)
    /// buffer of <paramref name="dw"/> x <paramref name="dh"/> whose alpha carries the edge coverage.</summary>
    public static byte[] Resize(byte[] src, int sw, int sh, int srcStride, int dw, int dh)
    {
        var dst = new byte[dw * dh * 4];
        if (sw == dw && sh == dh)
        {
            for (int y = 0; y < sh; y++)
                Buffer.BlockCopy(src, y * srcStride, dst, y * dw * 4, sw * 4);
            return dst;
        }

        var xt = BuildTaps(sw, dw);
        var yt = BuildTaps(sh, dh);

        // Horizontal pass into an 8-bit premultiplied intermediate (dw x sh).
        var mid = new byte[dw * sh * 4];
        var srcCopy = src;
        void Row(int y)
        {
            int rowBase = y * srcStride;
            for (int x = 0; x < dw; x++)
            {
                double b = 0, g = 0, r = 0, a = 0;
                var w = xt.Weights[x];
                int i = rowBase + xt.Start[x] * 4;
                for (int k = 0; k < w.Length; k++, i += 4)
                {
                    double pa = srcCopy[i + 3];
                    if (pa == 0) continue;
                    double wk = w[k];
                    double f = wk * pa / 255.0;
                    b += f * srcCopy[i];
                    g += f * srcCopy[i + 1];
                    r += f * srcCopy[i + 2];
                    a += wk * pa;
                }
                a = Math.Clamp(Math.Round(a), 0, 255);
                int o = (y * dw + x) * 4;
                mid[o] = (byte)Math.Clamp(Math.Round(b), 0, a);
                mid[o + 1] = (byte)Math.Clamp(Math.Round(g), 0, a);
                mid[o + 2] = (byte)Math.Clamp(Math.Round(r), 0, a);
                mid[o + 3] = (byte)a;
            }
        }
        if ((long)sw * sh > 1 << 16)
            Parallel.For(0, sh, Row);
        else
            for (int y = 0; y < sh; y++) Row(y);

        // Vertical pass, then back to non-premultiplied the way GDI+ does it: 8-bit alpha (truncated),
        // 8-bit premultiplied colour (rounded), times a truncated 16.16 reciprocal of alpha. That makes
        // partially covered white edge pixels 254, not 255 — it matters on near-uniform images, where
        // histogram equalization stretches a 1-level difference to full contrast.
        for (int y = 0; y < dh; y++)
        {
            var w = yt.Weights[y];
            int s0 = yt.Start[y];
            for (int x = 0; x < dw; x++)
            {
                double b = 0, g = 0, r = 0, a = 0;
                int i = (s0 * dw + x) * 4;
                for (int k = 0; k < w.Length; k++, i += dw * 4)
                {
                    double wk = w[k];
                    b += wk * mid[i];
                    g += wk * mid[i + 1];
                    r += wk * mid[i + 2];
                    a += wk * mid[i + 3];
                }
                int o = (y * dw + x) * 4;
                int alpha = (int)Math.Clamp(a + 1e-7, 0, 255);
                if (alpha == 0)
                    continue;
                long reciprocal = (255L << 16) / alpha;
                dst[o] = (byte)((Math.Clamp((int)Math.Round(b), 0, alpha) * reciprocal) >> 16);
                dst[o + 1] = (byte)((Math.Clamp((int)Math.Round(g), 0, alpha) * reciprocal) >> 16);
                dst[o + 2] = (byte)((Math.Clamp((int)Math.Round(r), 0, alpha) * reciprocal) >> 16);
                dst[o + 3] = (byte)alpha;
            }
        }
        return dst;
    }
}
