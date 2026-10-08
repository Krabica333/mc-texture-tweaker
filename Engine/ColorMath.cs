using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace TextureTinter.Engine;

public static class ColorMath
{
    public static readonly Vector3 Luma = new(0.299f, 0.587f, 0.114f);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 HexToRgb(string hex)
    {
        hex = hex.TrimStart('#');
        return new Vector3(
            System.Convert.ToInt32(hex.Substring(0, 2), 16),
            System.Convert.ToInt32(hex.Substring(2, 2), 16),
            System.Convert.ToInt32(hex.Substring(4, 2), 16));
    }

    public static Vector3 SrgbToOklab(Vector3 rgb)
    {
        Vector3 c = Vector3.Clamp(rgb, Vector3.Zero, new Vector3(255f)) / 255f;
        Vector3 lin = new(
            c.X <= 0.04045f ? c.X / 12.92f : MathF.Pow((c.X + 0.055f) / 1.055f, 2.4f),
            c.Y <= 0.04045f ? c.Y / 12.92f : MathF.Pow((c.Y + 0.055f) / 1.055f, 2.4f),
            c.Z <= 0.04045f ? c.Z / 12.92f : MathF.Pow((c.Z + 0.055f) / 1.055f, 2.4f));

        float l = 0.4122214708f * lin.X + 0.5363325363f * lin.Y + 0.0514459929f * lin.Z;
        float m = 0.2119034982f * lin.X + 0.6806995451f * lin.Y + 0.1073969566f * lin.Z;
        float s = 0.0883024619f * lin.X + 0.2817188376f * lin.Y + 0.6299787005f * lin.Z;
        l = MathF.Cbrt(l); m = MathF.Cbrt(m); s = MathF.Cbrt(s);

        return new Vector3(
            0.2104542553f * l + 0.7936177850f * m - 0.0040720468f * s,
            1.9779984951f * l - 2.4285922050f * m + 0.4505937099f * s,
            0.0259040371f * l + 0.7827717662f * m - 0.8086757660f * s);
    }

    public static Vector3 OklabToSrgb(Vector3 lab)
    {
        lab.X = Math.Clamp(lab.X, 0f, 1f);
        float l, m, s;
        Vector3 lin = default;
        for (int i = 0; i < 12; i++)
        {
            l = lab.X + 0.3963377774f * lab.Y + 0.2158037573f * lab.Z;
            m = lab.X - 0.1055613458f * lab.Y - 0.0638541728f * lab.Z;
            s = lab.X - 0.0894841775f * lab.Y - 1.2914855480f * lab.Z;
            l = l * l * l; m = m * m * m; s = s * s * s;
            lin = new Vector3(
                +4.0767416621f * l - 3.3077115913f * m + 0.2309699292f * s,
                -1.2684380046f * l + 2.6097574011f * m - 0.3413193965f * s,
                -0.0041960863f * l - 0.7034186147f * m + 1.7076147010f * s);
            if (lin.X is > -0.002f and < 1.002f &&
                lin.Y is > -0.002f and < 1.002f &&
                lin.Z is > -0.002f and < 1.002f) break;
            lab.Y *= 0.8f; lab.Z *= 0.8f;
        }
        var c = Vector3.Clamp(lin, Vector3.Zero, Vector3.One);
        return new Vector3(
            c.X <= 0.0031308f ? c.X * 12.92f : 1.055f * MathF.Pow(c.X, 1f / 2.4f) - 0.055f,
            c.Y <= 0.0031308f ? c.Y * 12.92f : 1.055f * MathF.Pow(c.Y, 1f / 2.4f) - 0.055f,
            c.Z <= 0.0031308f ? c.Z * 12.92f : 1.055f * MathF.Pow(c.Z, 1f / 2.4f) - 0.055f) * 255f;
    }

    public static Vector3 RgbToHsv(Vector3 rgb01)
    {
        float max = MathF.Max(rgb01.X, MathF.Max(rgb01.Y, rgb01.Z));
        float min = MathF.Min(rgb01.X, MathF.Min(rgb01.Y, rgb01.Z));
        float d = max - min;
        float h = 0f;
        if (d > 1e-6f)
        {
            if (max == rgb01.X) h = ((rgb01.Y - rgb01.Z) / d) % 6f;
            else if (max == rgb01.Y) h = (rgb01.Z - rgb01.X) / d + 2f;
            else h = (rgb01.X - rgb01.Y) / d + 4f;
            h /= 6f;
            if (h < 0) h += 1f;
        }
        float s = max > 1e-6f ? d / max : 0f;
        return new Vector3(h, s, max);
    }

    public static Vector3 HsvToRgb(Vector3 hsv)
    {
        float h = hsv.X * 6f;
        int i = (int)MathF.Floor(h) % 6;
        if (i < 0) i += 6;
        float f = h - MathF.Floor(h);
        float p = hsv.Z * (1f - hsv.Y);
        float q = hsv.Z * (1f - f * hsv.Y);
        float t = hsv.Z * (1f - (1f - f) * hsv.Y);
        return i switch
        {
            0 => new Vector3(hsv.Z, t, p),
            1 => new Vector3(q, hsv.Z, p),
            2 => new Vector3(p, hsv.Z, t),
            3 => new Vector3(p, q, hsv.Z),
            4 => new Vector3(t, p, hsv.Z),
            _ => new Vector3(hsv.Z, p, q),
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Wrap01(float x) => x - MathF.Floor(x);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float WrapHalf(float x) => (x + 0.5f) % 1f - 0.5f;

    public static float Curve3(float x, float xm, float y0, float ym, float y1)
    {
        if (x <= xm)
        {
            float t = x / xm;
            float d0 = (ym - y0) / xm, d1 = (y1 - ym) / (1 - xm);
            float m0 = d0, m1 = d0 * d1 > 0 ? 2f * d0 * d1 / (d0 + d1) : 0f;
            return Hermite(t, y0, ym, m0 * xm, m1 * xm);
        }
        else
        {
            float t = (x - xm) / (1 - xm);
            float d0 = (ym - y0) / xm, d1 = (y1 - ym) / (1 - xm);
            float m0 = d0 * d1 > 0 ? 2f * d0 * d1 / (d0 + d1) : 0f;
            float m1 = d1;
            return Hermite(t, ym, y1, m0 * (1 - xm), m1 * (1 - xm));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Hermite(float t, float a, float b, float m0, float m1)
    {
        float t2 = t * t, t3 = t2 * t;
        return (2 * t3 - 3 * t2 + 1) * a + (t3 - 2 * t2 + t) * m0
             + (-2 * t3 + 3 * t2) * b + (t3 - t2) * m1;
    }
}