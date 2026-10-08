using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using TextureTinter.Models;

namespace TextureTinter.Minecraft;

public sealed class ScanResult
{
    public Dictionary<string, BlockInfo> Blocks { get; } = new();
    public Dictionary<string, (int srcIdx, string path)> Textures { get; } = new();
    public List<(string Label, ZipArchive Zip)> Sources { get; } = new();
    public List<string> Warnings { get; } = new();
}

public static class JarScanner
{
    static readonly System.Text.RegularExpressions.Regex AssetRx =
        new(@"^assets/([^/]+)/(blockstates|models|textures|lang)/(.+)$",
            System.Text.RegularExpressions.RegexOptions.Compiled);

    static readonly string[] VanillaColors = {
        "light_blue","light_gray","white","orange","magenta","yellow","lime","pink","gray",
        "cyan","purple","blue","brown","green","red","black"
    };

    public static byte[]? GetTextureBytes(ScanResult scan, string tid)
    {
        if (!scan.Textures.TryGetValue(tid, out var loc)) return null;
        if (loc.srcIdx < 0 || loc.srcIdx >= scan.Sources.Count) return null;
        var zip = scan.Sources[loc.srcIdx].Zip;
        var entry = zip.GetEntry(loc.path);
        if (entry == null) return null;
        using var s = entry.Open();
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    public static async Task<ScanResult> ScanAsync(
        string? clientJar, string? mcDir, bool includeMods, IEnumerable<string> extraJars,
        Action<string> progress)
    {
        var res = new ScanResult();
        var sources = new List<(string label, ZipArchive zip)>();

        void AddSource(string label, ZipArchive zip)
        {
            sources.Add((label, zip));
            foreach (var e in zip.Entries)
                if (e.FullName.EndsWith(".jar") &&
                    (e.FullName.StartsWith("META-INF/jarjar/") || e.FullName.StartsWith("META-INF/jars/")))
                {
                    try
                    {
                        using var ms = new MemoryStream();
                        using (var s = e.Open()) s.CopyTo(ms);
                        ms.Position = 0;
                        sources.Add((label + " > " + Path.GetFileName(e.FullName),
                                     new ZipArchive(ms, ZipArchiveMode.Read)));
                    }
                    catch { }
                }
        }

        if (clientJar != null && File.Exists(clientJar))
            AddSource("Minecraft " + Path.GetFileNameWithoutExtension(clientJar), ZipFile.OpenRead(clientJar));
        else
            res.Warnings.Add("No game jar: only mods will be scanned.");

        if (includeMods && mcDir != null)
        {
            var md = Path.Combine(mcDir, "mods");
            if (Directory.Exists(md))
                foreach (var f in Directory.EnumerateFiles(md, "*.jar").OrderBy(x => x))
                    try { AddSource(ModLabel(f), ZipFile.OpenRead(f)); }
                    catch { res.Warnings.Add("Could not open " + Path.GetFileName(f)); }
        }
        foreach (var f in extraJars)
            try { AddSource(ModLabel(f), ZipFile.OpenRead(f)); }
            catch { res.Warnings.Add("Could not open " + Path.GetFileName(f)); }

        // 1) Index
        var bstates = new Dictionary<string, (int, string)>();
        var models = new Dictionary<string, (int, string)>();
        var anim = new HashSet<string>();
        var lang = new Dictionary<string, string>();

        for (int si = 0; si < sources.Count; si++)
        {
            var (label, zip) = sources[si];
            progress($"Indexing {label} ({si + 1}/{sources.Count})");
            foreach (var e in zip.Entries)
            {
                var m = AssetRx.Match(e.FullName);
                if (!m.Success) continue;
                var ns = m.Groups[1].Value; var what = m.Groups[2].Value; var rest = m.Groups[3].Value;
                switch (what)
                {
                    case "blockstates" when rest.EndsWith(".json"):
                        bstates.TryAdd($"{ns}:{rest[..^5]}", (si, e.FullName)); break;
                    case "models" when rest.EndsWith(".json"):
                        models.TryAdd($"{ns}:{rest[..^5]}", (si, e.FullName)); break;
                    case "textures" when rest.EndsWith(".png"):
                        res.Textures.TryAdd($"{ns}:{rest[..^4]}", (si, e.FullName)); break;
                    case "textures" when rest.EndsWith(".png.mcmeta"):
                        anim.Add($"{ns}:{rest[..^11]}"); break;
                    case "lang" when rest == "en_us.json":
                        try
                        {
                            using var s = e.Open();
                            var doc = JsonDocument.Parse(s);
                            foreach (var p in doc.RootElement.EnumerateObject())
                                lang.TryAdd(p.Name, p.Value.GetString() ?? "");
                        }
                        catch { }
                        break;
                }
            }
        }
        res.Sources.AddRange(sources);

        // 2) Resolve a model → slots, tinted flag, root-parent chain
        string Nid(string s) => s.Contains(':') ? s : "minecraft:" + s;
        var mcache = new Dictionary<string, (Dictionary<string, string> slots, bool tinted, string? root)>();

        (Dictionary<string, string> slots, bool tinted, string? root) Resolve(string mid, int guard = 0)
        {
            if (mcache.TryGetValue(mid, out var c)) return c;
            if (guard > 24) return (new(), false, null);
            if (!models.TryGetValue(mid, out var loc)) return (new(), false, null);

            var zip = sources[loc.Item1].zip;
            var entry = zip.GetEntry(loc.Item2);
            if (entry == null) return (new(), false, null);
            using var s = entry.Open();
            using var doc = JsonDocument.Parse(s);
            var root = doc.RootElement;

            var texMap = new Dictionary<string, string>();
            if (root.TryGetProperty("textures", out var texes))
                foreach (var p in texes.EnumerateObject()) texMap[p.Name] = p.Value.GetString() ?? "";

            bool tinted = false;
            if (root.TryGetProperty("elements", out var elems) && elems.ValueKind == JsonValueKind.Array)
                foreach (var el in elems.EnumerateArray())
                    if (el.TryGetProperty("faces", out var faces))
                        foreach (var f in faces.EnumerateObject())
                            if (f.Value.TryGetProperty("tintindex", out _)) tinted = true;

            Dictionary<string, string> parentSlots = new();
            bool parentTinted = false;
            string? parentRoot = null;
            string? parentRef = null;
            if (root.TryGetProperty("parent", out var par) && par.ValueKind == JsonValueKind.String)
            {
                parentRef = Nid(par.GetString()!);
                var (ps, pt, pr) = Resolve(parentRef, guard + 1);
                parentSlots = ps;
                parentTinted = pt;
                parentRoot = pr;   // will be null at the top of the chain; that's fine, we'll patch below
            }

            string? Deref(string v, int g = 0)
            {
                while (g++ < 12 && v.StartsWith('#'))
                    v = texMap.TryGetValue(v[1..], out var nv) ? nv : "";
                return v.StartsWith('#') || v.Length == 0 ? null : Nid(v);
            }

            foreach (var kv in parentSlots) texMap.TryAdd(kv.Key, kv.Value);
            var slots = new Dictionary<string, string>();
            foreach (var (k, v) in texMap)
            {
                var d = Deref(v);
                if (d != null) slots[k] = d;
            }

            // Root parent = self if no parent, else the deepest root the parent chain found.
            string? thisRoot = parentRoot ?? (parentRef is null ? mid : null);

            var result = (slots, tinted || parentTinted, thisRoot);
            mcache[mid] = result;
            return result;
        }

        progress("Reading blocks...");
        foreach (var (bid, loc) in bstates)
        {
            var zip = sources[loc.Item1].zip;
            var entry = zip.GetEntry(loc.Item2);
            if (entry == null) continue;
            JsonDocument doc;
            try { using var s = entry.Open(); doc = JsonDocument.Parse(s); }
            catch { continue; }
            using (doc)
            {
                var root = doc.RootElement;
                var modelIds = new List<string>();
                var props = new Dictionary<string, HashSet<string>>();

                void Use(JsonElement e)
                {
                    if (e.ValueKind == JsonValueKind.Array) { foreach (var x in e.EnumerateArray()) Use(x); return; }
                    if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty("model", out var m))
                    {
                        var nm = Nid(m.GetString() ?? "");
                        if (!modelIds.Contains(nm)) modelIds.Add(nm);
                    }
                }

                if (root.TryGetProperty("variants", out var variants))
                    foreach (var v in variants.EnumerateObject())
                    {
                        Use(v.Value);
                        foreach (var kv in v.Name.Split(','))
                        {
                            var idx = kv.IndexOf('=');
                            if (idx > 0)
                            {
                                var k = kv[..idx]; var val = kv[(idx + 1)..];
                                if (!props.TryGetValue(k, out var set)) props[k] = set = new();
                                set.Add(val);
                            }
                        }
                    }
                if (root.TryGetProperty("multipart", out var multipart))
                    foreach (var part in multipart.EnumerateArray())
                    {
                        if (part.TryGetProperty("apply", out var ap)) Use(ap);
                        if (part.TryGetProperty("when", out var wh))
                        {
                            void WalkWhen(JsonElement w)
                            {
                                if (w.ValueKind == JsonValueKind.Object)
                                    foreach (var p in w.EnumerateObject())
                                    {
                                        if (p.Name == "OR" || p.Name == "AND") WalkWhen(p.Value);
                                        else if (p.Value.ValueKind == JsonValueKind.String)
                                        {
                                            if (!props.TryGetValue(p.Name, out var set)) props[p.Name] = set = new();
                                            foreach (var x in p.Value.GetString()!.Split('|')) set.Add(x);
                                        }
                                    }
                            }
                            WalkWhen(wh);
                        }
                    }

                var slots = new Dictionary<string, string>();
                bool tinted = false;
                string? rootParent = null;
                foreach (var mid in modelIds)
                {
                    var (ms, mt, mr) = Resolve(mid);
                    tinted |= mt;
                    // Prefer the first non-null root we see.
                    if (rootParent is null && mr is not null) rootParent = mr;
                    foreach (var (k, v) in ms)
                        if (!slots.ContainsKey(k) && !slots.ContainsValue(v)) slots[k] = v;
                }

                if (slots.Count == 0) continue;
                var ns = bid.Split(':')[0];
                var path = bid.Split(':')[1];
                var name = lang.TryGetValue($"block.{ns}.{path.Replace('/', '.')}", out var lname)
                    ? lname : TitleCase(path);

                var flags = new List<string>();
                if (tinted) flags.Add("tinted");
                if (slots.Values.Any(t => anim.Contains(t))) flags.Add("animated");
                if (slots.Values.Any(t => !res.Textures.ContainsKey(t))) flags.Add("missing texture");

                var shape = ClassifyShape(rootParent);
                if (shape == "Cross") flags.Add("cross");

                res.Blocks[bid] = new BlockInfo
                {
                    Name = name,
                    Source = sources[loc.Item1].label,
                    Slots = slots,
                    Props = props.ToDictionary(k => k.Key, v => v.Value.OrderBy(x => x).ToList()),
                    Flags = flags,
                    RootParent = rootParent,
                    ModelShape = shape,
                };
            }
        }

        // 3) Family detection
        var fam = new Dictionary<(string, string), Dictionary<string, string>>();
        foreach (var bid in res.Blocks.Keys.ToList())
        {
            var (ns, path) = Split(bid);
            foreach (var c in VanillaColors)
                if (path.StartsWith(c + "_"))
                {
                    var stem = path[(c.Length + 1)..];
                    if (!fam.TryGetValue((ns, stem), out var d)) fam[(ns, stem)] = d = new();
                    d[bid] = c;
                    break;
                }
        }
        foreach (var ((ns, stem), mem) in fam)
            if (mem.Count >= 4)
                foreach (var (bid, c) in mem)
                {
                    res.Blocks[bid].Family = stem;
                    res.Blocks[bid].Color = c;
                }

        return res;

        static (string, string) Split(string s) { var i = s.IndexOf(':'); return (s[..i], s[(i + 1)..]); }
        static string TitleCase(string s) => string.Join(' ', s.Split('_').Select(w => char.ToUpper(w[0]) + w[1..]));
    }

    // Classify geometry from the root parent id.
    static string ClassifyShape(string? rootParent)
    {
        if (string.IsNullOrEmpty(rootParent)) return "CubeAll";
        var short_ = rootParent.Split(':').Last();      // "block/cross"

        if (short_.EndsWith("cross")) return "Cross";
        if (short_.EndsWith("cube_bottom_top")) return "BottomTop";
        if (short_.EndsWith("cube_column") || short_.EndsWith("cube_column_horizontal")) return "Column";
        if (short_.EndsWith("orientable") || short_.EndsWith("orientable_with_bottom") || short_.EndsWith("orientable_vertical"))
            return "Orientable";
        if (short_.EndsWith("carpet") || short_.EndsWith("thin_block") || short_.EndsWith("lily_pad")) return "Flat";
        // cube / cube_all / cube_mirrored_all / anything else → cube
        return "CubeAll";
    }

    static string ModLabel(string f)
    {
        try
        {
            using var zip = ZipFile.OpenRead(f);
            var fab = zip.GetEntry("fabric.mod.json");
            if (fab != null)
                using (var s = fab.Open())
                {
                    var doc = JsonDocument.Parse(s);
                    if (doc.RootElement.TryGetProperty("name", out var n)) return n.GetString() ?? Path.GetFileName(f);
                }
        }
        catch { }
        return Path.GetFileName(f);
    }
}