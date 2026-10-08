using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using McTextureTweaker.Engine;
using McTextureTweaker.Models;

namespace McTextureTweaker.Services;

public static class ExportService
{
    public static async Task<int> ExportAllAsync(
        ProjectConfig cfg, Func<string, TexImage> loadImage,
        IProgress<(int done, int total, string file)>? progress = null,
        CancellationToken ct = default)
    {
        var jobs = new List<(string key, TextureEntry t)>();
        foreach (var (k, t) in cfg.Textures)
            if (t.Category != null && cfg.Categories.ContainsKey(t.Category))
                jobs.Add((k, t));

        int total = jobs.Count * cfg.Dyes.Count;
        int done = 0, written = 0;

        foreach (var (key, entry) in jobs)
        {
            ct.ThrowIfCancellationRequested();
            var img = loadImage(key);
            string texName = entry.Name ?? Path.GetFileNameWithoutExtension(key);

            foreach (var (dyeName, dye) in cfg.Dyes)
            {
                ct.ThrowIfCancellationRequested();
                var profile = RenderPipeline.ProfileFor(entry, dyeName);
                var cat = cfg.Categories[entry.Category!];
                string hex = cat.Colors.TryGetValue(dyeName, out var h) ? h : dye.Color;
                var color = ColorMath.HexToRgb(hex);

                cfg.Compensation.TryGetValue($"{key}|{entry.Category}|{dyeName}", out var comp);

                var outImg = await Task.Run(() => RenderPipeline.Render(img, color, profile, comp), ct);

                string fn = cfg.Pattern
                    .Replace("{dye}", dyeName)
                    .Replace("{name}", texName)
                    .Replace("{cat}", entry.Category!);
                string dest = Path.Combine(cfg.ExportDir!, fn);
                await Task.Run(() => outImg.ToBitmap().Save(dest), ct);

                done++; written++;
                progress?.Report((done, total, fn));
            }
        }
        return written;
    }
}