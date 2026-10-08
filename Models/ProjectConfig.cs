using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace McTextureTweaker.Models;

public sealed class ProjectConfig
{
    public Dictionary<string, Dye> Dyes { get; set; } = new();
    public Dictionary<string, Category> Categories { get; set; } = new();
    public Dictionary<string, TextureEntry> Textures { get; set; } = new();
    public Dictionary<string, CompensationSettings> Compensation { get; set; } = new();
    public Dictionary<string, BlockInfo> Blocks { get; set; } = new();
    public string? ExportDir { get; set; }
    public string Pattern { get; set; } = "{dye}_{name}.png";
    public MinecraftSettings Mc { get; set; } = new();

    public static ProjectConfig CreateDefault()
    {
        var cfg = new ProjectConfig();
        foreach (var (name, hex) in Models.Dyes.DefaultPalette)
            cfg.Dyes[name] = new Dye { Color = hex };
        foreach (var (name, k) in new (string, float)[] { ("wool", 1.0f), ("brick", 0.8f), ("text", 1.15f) })
        {
            var cat = new Category();
            foreach (var (d, h) in Models.Dyes.DefaultPalette)
                cat.Colors[d] = Shade(h, k);
            cfg.Categories[name] = cat;
        }
        return cfg;
    }

    public static string Shade(string hex, float f)
    {
        int r = System.Convert.ToInt32(hex.Substring(1, 2), 16);
        int g = System.Convert.ToInt32(hex.Substring(3, 2), 16);
        int b = System.Convert.ToInt32(hex.Substring(5, 2), 16);
        return $"#{Clamp((int)(r * f)):X2}{Clamp((int)(g * f)):X2}{Clamp((int)(b * f)):X2}";
        static int Clamp(int v) => v > 255 ? 255 : v < 0 ? 0 : v;
    }
}

public sealed class MinecraftSettings
{
    public string? Dir { get; set; }
    public string? Version { get; set; }
    public bool IncludeMods { get; set; } = true;
    public List<string> ExtraJars { get; set; } = new();
    public string? ClientJarOverride { get; set; }
}

public sealed class Dye
{
    public string Color { get; set; } = "#FFFFFF";
    public string? Icon { get; set; }
}

public sealed class Category
{
    public string? Icon { get; set; }
    public Dictionary<string, string> Colors { get; set; } = new();
}

public sealed class BlockInfo
{
    public string Name { get; set; } = "";
    public string Source { get; set; } = "";
    public Dictionary<string, List<string>> Props { get; set; } = new();
    public Dictionary<string, string> Slots { get; set; } = new();
    public List<string> Flags { get; set; } = new();
    public string? Family { get; set; }
    public string? Color { get; set; }

    /// <summary>Root parent model id, e.g. "minecraft:block/cross". null if unknown.</summary>
    public string? RootParent { get; set; }

    /// <summary>Mesh shape derived from RootParent: CubeAll / BottomTop / Column / Orientable / Cross / Flat.</summary>
    public string ModelShape { get; set; } = "CubeAll";
}