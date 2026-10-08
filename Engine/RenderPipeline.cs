using System;
using System.Numerics;
using TextureTinter.Models;

namespace TextureTinter.Engine;

public static class RenderPipeline
{
    public static RecolorSettings ProfileFor(TextureEntry entry, string dyeName)
    {
        foreach (var ex in entry.Exceptions)
            if (ex.Dyes.Contains(dyeName))
                return ex;
        return entry.Settings;
    }

    public static TexImage Render(TexImage source, Vector3 dyeColor, RecolorSettings cfg,
                                  CompensationSettings? comp)
    {
        var img = source.Clone();
        RecolorEngine.Adjust(img, cfg.Pre);

        TexImage result;
        var m2 = cfg.Method2;
        var blended = m2.HasValue && m2.Value != cfg.Method;

        if (blended)
        {
            var a = Single(img, dyeColor, cfg, comp, false);
            var b = Single(img, dyeColor, cfg, comp, true);
            float mix = cfg.BlendPct / 100f;
            var arr = new float[a.Data.Length];
            for (int i = 0; i < arr.Length; i++) arr[i] = a.Data[i] * (1 - mix) + b.Data[i] * mix;
            var merged = new TexImage(a.Width, a.Height);
            Array.Copy(arr, merged.Data, arr.Length);
            result = merged;
        }
        else
        {
            result = Single(img, dyeColor, cfg, comp, false);
        }

        RecolorEngine.Adjust(result, cfg.Post);
        return result;
    }

    private static TexImage Single(TexImage img, Vector3 target, RecolorSettings cfg, CompensationSettings? comp, bool second)
    {
        var method = second ? cfg.Method2 ?? cfg.Method : cfg.Method;
        float tint = second ? cfg.Tint2 : cfg.Tint;
        float sat = second ? cfg.Sat2 : cfg.Sat;

        return method switch
        {
            RecolorMethod.Classic => RecolorEngine.RecolorClassic(img, target, tint, sat),
            RecolorMethod.Perceptual => RecolorEngine.RecolorPerceptual(img, target, tint,
                second ? cfg.HueSplit2 : cfg.HueSplit,
                second ? cfg.ShadingGain2 : cfg.ShadingGain, sat),
            RecolorMethod.Pick => RecolorEngine.RecolorPick(img, target,
                cfg.ReferencePixel is { Length: 2 } rp
                    ? GetPixelRgb(img, rp[0], rp[1])
                    : RecolorEngine.WeightedAvgRgb(img),
                second ? cfg.Tolerance2 : cfg.Tolerance, tint, sat),
            RecolorMethod.Hsv => RecolorEngine.RecolorHsv(img, target,
                second ? cfg.FlattenHue2 : cfg.FlattenHue,
                second ? cfg.FlattenSat2 : cfg.FlattenSat,
                second ? cfg.FlattenVal2 : cfg.FlattenVal,
                second ? cfg.ShadingGain2 : cfg.ShadingGain,
                second ? cfg.HueSplit2 : cfg.HueSplit,
                second ? cfg.ShadowSat2 : cfg.ShadowSat,
                second ? cfg.ExtremeComp2 : cfg.ExtremeComp,
                second ? cfg.BrightShadowDark2 : cfg.BrightShadowDark),
            _ => RecolorEngine.RecolorAverage(img, target, tint, comp, sat),
        };
    }

    private static Vector3 GetPixelRgb(TexImage img, int x, int y)
    {
        if (x < 0 || y < 0 || x >= img.Width || y >= img.Height) return Vector3.Zero;
        int i = (y * img.Width + x) * 4;
        return new Vector3(img.Data[i], img.Data[i + 1], img.Data[i + 2]);
    }
}