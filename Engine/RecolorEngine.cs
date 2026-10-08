using System;
using System.Numerics;
using System.Threading.Tasks;
using McTextureTweaker.Models;

namespace McTextureTweaker.Engine;

public static class RecolorEngine
{
    public static Vector3 WeightedAvgRgb(TexImage img)
    {
        double r = 0, g = 0, b = 0, wsum = 0;
        var gate = new object();

        Parallel.For(0, img.Height,
            () => (0d, 0d, 0d, 0d),
            (y, _, acc) =>
            {
                int stride = img.Width * 4;
                int off = y * stride;
                for (int x = 0; x < img.Width; x++)
                {
                    float a = img.Data[off + x * 4 + 3];
                    double w = a / 255.0 + 1e-6;
                    acc.Item1 += img.Data[off + x * 4 + 0] * w;
                    acc.Item2 += img.Data[off + x * 4 + 1] * w;
                    acc.Item3 += img.Data[off + x * 4 + 2] * w;
                    acc.Item4 += w;
                }
                return acc;
            },
            acc =>
            {
                lock (gate) { r += acc.Item1; g += acc.Item2; b += acc.Item3; wsum += acc.Item4; }
            });

        if (wsum <= 0) return Vector3.Zero;
        return new Vector3((float)(r / wsum), (float)(g / wsum), (float)(b / wsum));
    }

    public static (CompensationSettings cp, float td, float tb) CompRamps(float y, CompensationSettings? c)
    {
        c ??= new CompensationSettings();
        float td = MathF.Pow(Math.Clamp((c.DarkStart - y) / MathF.Max(c.DarkStart, 1f), 0f, 1f), c.Curve);
        float tb = MathF.Pow(Math.Clamp((y - c.BrightStart) / MathF.Max(255f - c.BrightStart, 1f), 0f, 1f), c.Curve);
        return (c, td, tb);
    }

    // -------- Average match + compensation --------
    public static TexImage RecolorAverage(TexImage src, Vector3 target, float tint, CompensationSettings? comp, float sat)
    {
        var img = src.Clone();
        var d = img.Data;
        var avg = WeightedAvgRgb(img);
        float avgLum = MathF.Max(Vector3.Dot(avg, ColorMath.Luma), 1f);
        var (cp, td, tb) = CompRamps(Vector3.Dot(target, ColorMath.Luma), comp);

        Parallel.For(0, img.Height, y =>
        {
            int stride = img.Width * 4, off = y * stride;
            for (int x = 0; x < img.Width; x++)
            {
                int i = off + x * 4;
                if (d[i + 3] < 0.001f) continue;
                float lum = d[i] * 0.299f + d[i + 1] * 0.587f + d[i + 2] * 0.114f;
                float k = lum / avgLum;
                d[i] = target.X * k;
                d[i + 1] = target.Y * k;
                d[i + 2] = target.Z * k;
            }
        });

        var avg2 = WeightedAvgRgb(img);
        var norm = new Vector3(
            target.X / MathF.Max(avg2.X, 1f),
            target.Y / MathF.Max(avg2.Y, 1f),
            target.Z / MathF.Max(avg2.Z, 1f));
        float tLum = Vector3.Dot(target, ColorMath.Luma);

        Parallel.For(0, img.Height, y =>
        {
            int stride = img.Width * 4, off = y * stride;
            for (int x = 0; x < img.Width; x++)
            {
                int i = off + x * 4;
                if (d[i + 3] < 0.001f) continue;
                float nr = d[i] * norm.X, ng = d[i + 1] * norm.Y, nb = d[i + 2] * norm.Z;
                float lum = nr * 0.299f + ng * 0.587f + nb * 0.114f;
                bool up = lum > tLum;
                float factor = up ? 1f + cp.DarkBoost * td : 1f + cp.BrightBoost * tb;
                d[i] = target.X + (nr - target.X) * factor;
                d[i + 1] = target.Y + (ng - target.Y) * factor;
                d[i + 2] = target.Z + (nb - target.Z) * factor;
            }
        });

        var avg3 = WeightedAvgRgb(img);
        Parallel.For(0, img.Height, y =>
        {
            int stride = img.Width * 4, off = y * stride;
            for (int x = 0; x < img.Width; x++)
            {
                int i = off + x * 4;
                if (d[i + 3] < 0.001f) continue;
                d[i] = Math.Clamp(d[i] + (target.X - avg3.X), 0, 255);
                d[i + 1] = Math.Clamp(d[i + 1] + (target.Y - avg3.Y), 0, 255);
                d[i + 2] = Math.Clamp(d[i + 2] + (target.Z - avg3.Z), 0, 255);
            }
        });

        ApplyTint(img, src, target, tint, sat);
        return img;
    }

    // -------- Classic multiply --------
    public static TexImage RecolorClassic(TexImage src, Vector3 target, float tint, float sat)
    {
        var img = src.Clone();
        var d = img.Data;
        Parallel.For(0, img.Height, y =>
        {
            int stride = img.Width * 4, off = y * stride;
            for (int x = 0; x < img.Width; x++)
            {
                int i = off + x * 4;
                if (d[i + 3] < 0.001f) continue;
                float lum = (d[i] * 0.299f + d[i + 1] * 0.587f + d[i + 2] * 0.114f) / 255f;
                d[i] = lum * target.X;
                d[i + 1] = lum * target.Y;
                d[i + 2] = lum * target.Z;
            }
        });
        ApplyTint(img, src, target, tint, sat);
        return img;
    }

    // -------- Perceptual (OKLab) --------
    public static TexImage RecolorPerceptual(TexImage src, Vector3 target, float tint, float hueShift, float gain, float sat)
    {
        var img = src.Clone();
        var d = img.Data;
        var tLab = ColorMath.SrgbToOklab(target);

        double sumL = 0, sumW = 0;
        var gate = new object();
        Parallel.For(0, img.Height,
            () => (0d, 0d),
            (y, _, acc) =>
            {
                int stride = img.Width * 4, off = y * stride;
                for (int x = 0; x < img.Width; x++)
                {
                    if (d[off + x * 4 + 3] < 0.001f) continue;
                    var lab = ColorMath.SrgbToOklab(new Vector3(d[off + x * 4], d[off + x * 4 + 1], d[off + x * 4 + 2]));
                    float w = d[off + x * 4 + 3] / 255f + 1e-6f;
                    acc.Item1 += lab.X * w;
                    acc.Item2 += w;
                }
                return acc;
            },
            acc => { lock (gate) { sumL += acc.Item1; sumW += acc.Item2; } });
        float avgL = (float)(sumL / MathF.Max((float)sumW, 1e-6f));

        Parallel.For(0, img.Height, y =>
        {
            int stride = img.Width * 4, off = y * stride;
            for (int x = 0; x < img.Width; x++)
            {
                int i = off + x * 4;
                if (d[i + 3] < 0.001f) continue;
                var lab = ColorMath.SrgbToOklab(new Vector3(d[i], d[i + 1], d[i + 2]));
                float dL = (lab.X - avgL) * gain;
                float L = tLab.X + dL;
                float th = (float)(Math.PI / 180.0) * hueShift * dL / MathF.Max(0.001f, MathF.Abs(dL));
                float ca = MathF.Cos(th), sa = MathF.Sin(th);
                var newLab = new Vector3(L, tLab.Y * ca - tLab.Z * sa, tLab.Y * sa + tLab.Z * ca);
                var rgb = ColorMath.OklabToSrgb(newLab);
                d[i] = rgb.X; d[i + 1] = rgb.Y; d[i + 2] = rgb.Z;
            }
        });
        ApplyTint(img, src, target, tint, sat);
        return img;
    }

    // -------- Pick-a-pixel --------
    public static TexImage RecolorPick(TexImage src, Vector3 target, Vector3 reference, float tol, float tint, float sat)
    {
        var img = src.Clone();
        var d = img.Data;
        var rLab = ColorMath.SrgbToOklab(reference);
        var tLab = ColorMath.SrgbToOklab(target);
        float r0 = Math.Clamp(rLab.X, 0.02f, 0.98f);
        float t0 = Math.Clamp(tLab.X, 0.02f, 0.98f);
        float cR = MathF.Sqrt(rLab.Y * rLab.Y + rLab.Z * rLab.Z);
        float cT = MathF.Sqrt(tLab.Y * tLab.Y + tLab.Z * tLab.Z);
        Vector2 ur, ut;
        if (cR > 0.02f) { ur = new(rLab.Y / cR, rLab.Z / cR); ut = cT > 0.02f ? new(tLab.Y / cT, tLab.Z / cT) : ur; }
        else { ur = new(1, 0); ut = ur; }
        float scale = cR > 0.02f ? cT / cR : 1f;

        Parallel.For(0, img.Height, y =>
        {
            int stride = img.Width * 4, off = y * stride;
            for (int x = 0; x < img.Width; x++)
            {
                int i = off + x * 4;
                if (d[i + 3] < 0.001f) continue;
                var srcRgb = new Vector3(d[i], d[i + 1], d[i + 2]);
                var p = ColorMath.SrgbToOklab(srcRgb);
                float dist = MathF.Sqrt(
                    (p.X - rLab.X) * (p.X - rLab.X) +
                    (p.Y - rLab.Y) * (p.Y - rLab.Y) +
                    (p.Z - rLab.Z) * (p.Z - rLab.Z));
                float wt = tol >= 99.5f
                    ? 1f
                    : Math.Clamp(((tol / 100f) - dist) / (0.3f * (tol / 100f) + 1e-6f), 0f, 1f);
                float L = p.X <= r0
                    ? (p.X / r0) * t0
                    : t0 + ((p.X - r0) / (1 - r0)) * (1 - t0);
                var D = new Vector2(p.Y - rLab.Y, p.Z - rLab.Z);
                float proj = D.X * ur.X + D.Y * ur.Y;
                float perp = -D.X * ur.Y + D.Y * ur.X;
                float a = scale * proj * ut.X + scale * perp * -ut.Y;
                float b = scale * proj * ut.Y + scale * perp * ut.X;
                float bell = Math.Clamp(
                    MathF.Sqrt(L * (1 - L) / MathF.Max(t0 * (1 - t0), 1e-3f)), 0f, 1.5f);
                var lab = new Vector3(L, tLab.Y * bell + a, tLab.Z * bell + b);
                var mapped = ColorMath.OklabToSrgb(lab);
                d[i] = srcRgb.X * (1 - wt) + mapped.X * wt;
                d[i + 1] = srcRgb.Y * (1 - wt) + mapped.Y * wt;
                d[i + 2] = srcRgb.Z * (1 - wt) + mapped.Z * wt;
            }
        });
        ApplyTint(img, src, target, tint, sat);
        return img;
    }

    // -------- HSV match --------
    public static TexImage RecolorHsv(TexImage src, Vector3 target,
        float flatH, float flatS, float flatV, float gain,
        float hueSplit, float shadowSat, float comp, float shadowDark)
    {
        var img = src.Clone();
        var d = img.Data;
        int W = img.Width, H = img.Height;

        double sumS = 0, sumV = 0, wsum = 0, sinH = 0, cosH = 0;
        var gate = new object();
        Parallel.For(0, H, () => (0d, 0d, 0d, 0d, 0d), (y, _, acc) =>
        {
            int stride = W * 4, off = y * stride;
            for (int x = 0; x < W; x++)
            {
                int i = off + x * 4;
                if (d[i + 3] < 0.001f) continue;
                var hsv = ColorMath.RgbToHsv(new Vector3(d[i], d[i + 1], d[i + 2]) / 255f);
                double w = d[i + 3] / 255.0;
                acc.Item1 += hsv.Y * w;
                acc.Item2 += hsv.Z * w;
                acc.Item3 += w;
                acc.Item4 += Math.Sin(hsv.X * 2 * Math.PI) * w;
                acc.Item5 += Math.Cos(hsv.X * 2 * Math.PI) * w;
            }
            return acc;
        }, acc =>
        {
            lock (gate)
            {
                sumS += acc.Item1; sumV += acc.Item2; wsum += acc.Item3;
                sinH += acc.Item4; cosH += acc.Item5;
            }
        });
        if (wsum <= 0) return img;

        float mh = (float)(Math.Atan2(sinH, cosH) / (2 * Math.PI));
        if (mh < 0) mh += 1;
        float ms = (float)(sumS / wsum);
        float mv = (float)(sumV / wsum);

        var tHsv = ColorMath.RgbToHsv(target / 255f);

        float fH = flatH / 100f, fS = flatS / 100f, fV = flatV / 100f;
        float hueShift = ColorMath.WrapHalf(tHsv.X - mh);
        float satShift = tHsv.Y - ms;
        float valShift = tHsv.Z - mv;
        float shadowK = shadowDark / 100f;
        float shadowSatK = shadowSat / 100f;
        float extremeK = comp / 100f;

        float extreme = MathF.Max(
            Math.Clamp((MathF.Abs(tHsv.Z - 0.5f) - 0.25f) / 0.25f, 0, 1),
            Math.Clamp((tHsv.Y - 0.75f) / 0.25f, 0, 1));
        float ek = 1 - extremeK * extreme;
        hueShift *= ek;
        satShift *= ek;
        valShift *= ek;

        Parallel.For(0, H, y =>
        {
            int stride = W * 4, off = y * stride;
            for (int x = 0; x < W; x++)
            {
                int i = off + x * 4;
                if (d[i + 3] < 0.001f) continue;
                var hsv = ColorMath.RgbToHsv(new Vector3(d[i], d[i + 1], d[i + 2]) / 255f);

                float h0 = hsv.X + fH * ColorMath.WrapHalf(mh - hsv.X);
                float s0 = hsv.Y + fS * (ms - hsv.Y);
                float v0 = hsv.Z + fV * (mv - hsv.Z);

                float dev = (v0 - mv) * gain;
                float lt = Math.Max(0f, dev);
                float sh = Math.Max(0f, -dev);

                float hNew = ColorMath.Wrap01(h0 + hueShift + hueSplit / 360f * (lt - sh));
                float sNew = Math.Clamp(s0 + satShift + shadowSatK * 0.5f * sh, 0f, 1f);
                float vNew = Math.Clamp(v0 + valShift, 0f, 1f);
                vNew *= 1f - 0.6f * shadowK * tHsv.Z * (1f - v0);

                var rgb = ColorMath.HsvToRgb(new Vector3(hNew, sNew, Math.Clamp(vNew, 0, 1))) * 255f;
                d[i] = rgb.X; d[i + 1] = rgb.Y; d[i + 2] = rgb.Z;
            }
        });
        return img;
    }

    // -------- Tint / saturation transfer --------
    public static void ApplyTint(TexImage dst, TexImage src, Vector3 target, float tint, float sat)
    {
        if (tint == 0 && sat == 0) return;

        double sa = 0, sb = 0, sw = 0;
        var gate = new object();
        Parallel.For(0, src.Height,
            () => (0d, 0d, 0d),
            (y, _, acc) =>
            {
                int stride = src.Width * 4, off = y * stride;
                for (int x = 0; x < src.Width; x++)
                {
                    if (src.Data[off + x * 4 + 3] < 0.001f) continue;
                    var lab = ColorMath.SrgbToOklab(new Vector3(
                        src.Data[off + x * 4], src.Data[off + x * 4 + 1], src.Data[off + x * 4 + 2]));
                    float w = src.Data[off + x * 4 + 3] / 255f + 1e-6f;
                    acc.Item1 += lab.Y * w;
                    acc.Item2 += lab.Z * w;
                    acc.Item3 += w;
                }
                return acc;
            },
            acc => { lock (gate) { sa += acc.Item1; sb += acc.Item2; sw += acc.Item3; } });
        if (sw <= 0) return;

        Vector2 A = new((float)(sa / sw), (float)(sb / sw));
        var tLab = ColorMath.SrgbToOklab(target);
        Vector2 T = new(tLab.Y, tLab.Z);
        float cA = A.Length(), cT = T.Length();
        Vector2 ua = cA > 0.01f ? A / cA : (cT > 0.01f ? T / cT : new(1, 0));
        Vector2 ut = cT > 0.01f ? T / cT : ua;
        Vector2 perpUa = new(-ua.Y, ua.X), perpUt = new(-ut.Y, ut.X);

        Parallel.For(0, dst.Height, y =>
        {
            int stride = dst.Width * 4, off = y * stride;
            for (int x = 0; x < dst.Width; x++)
            {
                int i = off + x * 4;
                if (dst.Data[i + 3] < 0.001f) continue;
                var srcLab = ColorMath.SrgbToOklab(new Vector3(src.Data[i], src.Data[i + 1], src.Data[i + 2]));
                var dstLab = ColorMath.SrgbToOklab(new Vector3(dst.Data[i], dst.Data[i + 1], dst.Data[i + 2]));
                Vector2 D = new(srcLab.Y - A.X, srcLab.Z - A.Y);
                float dSat = sat * Vector2.Dot(D, ua);
                float dTint = tint * Vector2.Dot(D, perpUa);
                Vector2 delta = dSat * ut + dTint * perpUt;
                dstLab.Y += delta.X;
                dstLab.Z += delta.Y;
                var rgb = ColorMath.OklabToSrgb(dstLab);
                dst.Data[i] = rgb.X; dst.Data[i + 1] = rgb.Y; dst.Data[i + 2] = rgb.Z;
            }
        });
    }

    // -------- Finish (contrast + brightness) --------
    public static void Finish(TexImage img, float contrast, float brightness)
    {
        if (contrast == 1f && brightness == 0f) return;
        var avg = WeightedAvgRgb(img);
        float off = brightness * 2.55f;
        Parallel.For(0, img.Height, y =>
        {
            int stride = img.Width * 4, p = y * stride;
            for (int x = 0; x < img.Width; x++)
            {
                int i = p + x * 4;
                if (img.Data[i + 3] < 0.001f) continue;
                img.Data[i] = Math.Clamp(avg.X + (img.Data[i] - avg.X) * contrast + off, 0, 255);
                img.Data[i + 1] = Math.Clamp(avg.Y + (img.Data[i + 1] - avg.Y) * contrast + off, 0, 255);
                img.Data[i + 2] = Math.Clamp(avg.Z + (img.Data[i + 2] - avg.Z) * contrast + off, 0, 255);
            }
        });
    }

    // -------- Adjust (brightness, contrast, vibrance, clarity, hue) --------
    public static void Adjust(TexImage img, Adjustments adj)
    {
        if (adj.IsIdentity) return;

        if (adj.HueShift != 0f || adj.Vibrance != 0f)
        {
            Parallel.For(0, img.Height, y =>
            {
                int stride = img.Width * 4, off = y * stride;
                for (int x = 0; x < img.Width; x++)
                {
                    int i = off + x * 4;
                    if (img.Data[i + 3] < 0.001f) continue;
                    var lab = ColorMath.SrgbToOklab(new Vector3(img.Data[i], img.Data[i + 1], img.Data[i + 2]));
                    float C = MathF.Sqrt(lab.Y * lab.Y + lab.Z * lab.Z);
                    float hue = MathF.Atan2(lab.Z, lab.Y) + (float)(Math.PI / 180.0) * adj.HueShift;
                    float u = adj.Vibrance / 100f;
                    float f = Math.Clamp(C / 0.3f, 0f, 1f);
                    C *= 1f + u * (1f - f) * (u > 0 ? 1f : -1f);
                    lab.Y = C * MathF.Cos(hue);
                    lab.Z = C * MathF.Sin(hue);
                    var rgb = ColorMath.OklabToSrgb(lab);
                    img.Data[i] = rgb.X; img.Data[i + 1] = rgb.Y; img.Data[i + 2] = rgb.Z;
                }
            });
        }

        if (adj.Contrast != 1f)
        {
            var avg = WeightedAvgRgb(img);
            Parallel.For(0, img.Height, y =>
            {
                int stride = img.Width * 4, off = y * stride;
                for (int x = 0; x < img.Width; x++)
                {
                    int i = off + x * 4;
                    if (img.Data[i + 3] < 0.001f) continue;
                    img.Data[i] = avg.X + (img.Data[i] - avg.X) * adj.Contrast;
                    img.Data[i + 1] = avg.Y + (img.Data[i + 1] - avg.Y) * adj.Contrast;
                    img.Data[i + 2] = avg.Z + (img.Data[i + 2] - avg.Z) * adj.Contrast;
                }
            });
        }

        if (adj.Clarity != 0f)
        {
            var blurred = BlurRgb(img, Math.Max(1, Math.Max(img.Width, img.Height) / 12));
            Parallel.For(0, img.Height, y =>
            {
                int stride = img.Width * 4, off = y * stride;
                for (int x = 0; x < img.Width; x++)
                {
                    int i = off + x * 4;
                    if (img.Data[i + 3] < 0.001f) continue;
                    float lx = x / (float)(img.Width - 1 + 1e-6f);
                    float ly = y / (float)(img.Height - 1 + 1e-6f);
                    float xw = Math.Clamp(1 - MathF.Abs(lx - 0.5f) * 2, 0, 1);
                    float yw = Math.Clamp(1 - MathF.Abs(ly - 0.5f) * 2, 0, 1);
                    float k = adj.Clarity / 100f * 1.5f * xw * yw;
                    img.Data[i] += k * (img.Data[i] - blurred.Data[i]);
                    img.Data[i + 1] += k * (img.Data[i + 1] - blurred.Data[i + 1]);
                    img.Data[i + 2] += k * (img.Data[i + 2] - blurred.Data[i + 2]);
                }
            });
        }

        if (adj.Brightness != 0f)
        {
            float off = adj.Brightness * 2.55f;
            Parallel.For(0, img.Height, y =>
            {
                int stride = img.Width * 4, p = y * stride;
                for (int x = 0; x < img.Width; x++)
                {
                    int i = p + x * 4;
                    img.Data[i] = Math.Clamp(img.Data[i] + off, 0, 255);
                    img.Data[i + 1] = Math.Clamp(img.Data[i + 1] + off, 0, 255);
                    img.Data[i + 2] = Math.Clamp(img.Data[i + 2] + off, 0, 255);
                }
            });
        }
    }

    static TexImage BlurRgb(TexImage src, int r)
    {
        var k = new float[r * 2 + 1];
        float sigma = r / 1.5f, sum = 0;
        for (int i = -r; i <= r; i++) { float v = MathF.Exp(-(i * i) / (2 * sigma * sigma)); k[i + r] = v; sum += v; }
        for (int i = 0; i < k.Length; i++) k[i] /= sum;

        var temp = new TexImage(src.Width, src.Height);
        Parallel.For(0, src.Height, y =>
        {
            for (int x = 0; x < src.Width; x++)
            {
                float rr = 0, gg = 0, bb = 0;
                for (int i = -r; i <= r; i++)
                {
                    int xx = ((x + i) % src.Width + src.Width) % src.Width;
                    int idx = (y * src.Width + xx) * 4;
                    rr += src.Data[idx] * k[i + r];
                    gg += src.Data[idx + 1] * k[i + r];
                    bb += src.Data[idx + 2] * k[i + r];
                }
                int o = (y * src.Width + x) * 4;
                temp.Data[o] = rr; temp.Data[o + 1] = gg; temp.Data[o + 2] = bb; temp.Data[o + 3] = src.Data[o + 3];
            }
        });

        var dst = new TexImage(src.Width, src.Height);
        Parallel.For(0, src.Height, y =>
        {
            for (int x = 0; x < src.Width; x++)
            {
                float rr = 0, gg = 0, bb = 0;
                for (int i = -r; i <= r; i++)
                {
                    int yy = ((y + i) % src.Height + src.Height) % src.Height;
                    int idx = (yy * src.Width + x) * 4;
                    rr += temp.Data[idx] * k[i + r];
                    gg += temp.Data[idx + 1] * k[i + r];
                    bb += temp.Data[idx + 2] * k[i + r];
                }
                int o = (y * src.Width + x) * 4;
                dst.Data[o] = rr; dst.Data[o + 1] = gg; dst.Data[o + 2] = bb; dst.Data[o + 3] = src.Data[o + 3];
            }
        });
        return dst;
    }
}