using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using McTextureTweaker.Models;

namespace McTextureTweaker.Services;

public static class ProjectService
{
    /// <summary>
    /// Root folder for every project. Platform defaults:
    ///   Windows: %LOCALAPPDATA%\McTextureTweaker\projects
    ///   macOS:   ~/Library/Application Support/McTextureTweaker/projects
    ///   Linux:   ~/.local/share/McTextureTweaker/projects
    /// Falls back to the system temp folder if LocalApplicationData is unavailable.
    /// </summary>
    public static string RootDir { get; } = ResolveRoot();

    static string ResolveRoot()
    {
        try
        {
            var baseDir = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData,
                Environment.SpecialFolderOption.Create);

            if (!string.IsNullOrWhiteSpace(baseDir) && Directory.Exists(baseDir))
                return Path.Combine(baseDir, ".mctexturetweaker", "projects");
        }
        catch
        {
            // fall through to temp
        }

        // Fallback: system temp
        var tmp = Path.Combine(Path.GetTempPath(), ".mctexturetweaker", "projects");
        Directory.CreateDirectory(tmp);
        return tmp;
    }

    public static string ProjectDir(string name) => Path.Combine(RootDir, name);
    public static string ProjectFile(string name) => Path.Combine(ProjectDir(name), "project.json");

    public static string[] ListProjects()
    {
        Directory.CreateDirectory(RootDir);
        return Directory.GetDirectories(RootDir)
            .Where(d => File.Exists(Path.Combine(d, "project.json")))
            .OrderByDescending(d => File.GetLastWriteTimeUtc(Path.Combine(d, "project.json")))
            .Select(Path.GetFileName)
            .ToArray()!;
    }

    static readonly JsonSerializerOptions Opts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public static ProjectConfig Load(string name)
    {
        var f = ProjectFile(name);
        if (!File.Exists(f)) return ProjectConfig.CreateDefault();
        try
        {
            return JsonSerializer.Deserialize<ProjectConfig>(File.ReadAllText(f), Opts)
                   ?? ProjectConfig.CreateDefault();
        }
        catch
        {
            return ProjectConfig.CreateDefault();
        }
    }

    public static void Save(string name, ProjectConfig cfg)
    {
        var dir = ProjectDir(name);
        Directory.CreateDirectory(dir);

        // Atomic write: write to .tmp then swap. Avoids corrupt JSON if the app closes mid-write.
        var tmp = Path.Combine(dir, "project.json.tmp");
        File.WriteAllText(tmp, JsonSerializer.Serialize(cfg, Opts));
        File.Move(tmp, Path.Combine(dir, "project.json"), overwrite: true);
    }

    public static void Create(string name) => Save(name, ProjectConfig.CreateDefault());
}