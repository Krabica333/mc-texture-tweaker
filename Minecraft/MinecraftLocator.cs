using System;
using System.IO;
using System.Linq;

namespace TextureTinter.Minecraft;

public static class MinecraftLocator
{
    public static string? FindInstall()
    {
        var d = AppContext.BaseDirectory;
        for (int i = 0; i < 5; i++)
        {
            foreach (var cand in new[] { d, Path.Combine(d, ".minecraft"), Path.Combine(d, "minecraft") })
                if (Directory.Exists(Path.Combine(cand, "versions")) || Directory.Exists(Path.Combine(cand, "mods")))
                    return cand;
            d = Path.GetDirectoryName(d)!;
        }
        var appdata = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var cands = new[]
        {
            Path.Combine(appdata, ".minecraft"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".minecraft"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "minecraft"),
        };
        return cands.FirstOrDefault(Directory.Exists);
    }

    public static string? FindClientJar(string? mcDir, string? version)
    {
        if (mcDir is null) return null;
        if (version is not null)
        {
            var p = Path.Combine(mcDir, "versions", version, version + ".jar");
            if (File.Exists(p)) return p;
        }
        var vd = Path.Combine(mcDir, "versions");
        if (Directory.Exists(vd))
            foreach (var v in Directory.EnumerateDirectories(vd).OrderBy(x => x))
            {
                var jar = Path.Combine(v, Path.GetFileName(v) + ".jar");
                if (File.Exists(jar)) return jar;
            }
        return null;
    }
}