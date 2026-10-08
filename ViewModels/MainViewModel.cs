using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using McTextureTweaker.Engine;
using McTextureTweaker.Minecraft;
using McTextureTweaker.Models;
using McTextureTweaker.Rendering;
using McTextureTweaker.Services;
using McTextureTweaker.Views;

namespace McTextureTweaker.ViewModels;

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty] private int _stage = 1;
    [ObservableProperty] private string _status = "Ready";
    [ObservableProperty] private string? _projectName;
    [ObservableProperty] private ProjectConfig _config = ProjectConfig.CreateDefault();

    [ObservableProperty] private TextureItem? _selectedTextureItem;
    [ObservableProperty] private TextureEntry? _selectedTexture;
    [ObservableProperty] private DyeItem? _selectedDyeItem;
    [ObservableProperty] private string? _selectedDye = "white";
    [ObservableProperty] private PreviewTile? _selectedPreviewTile;
    [ObservableProperty] private int _exceptionTabIndex;
    [ObservableProperty] private bool _autoSpin = true;
    [ObservableProperty] private bool _isTextureMode;   // false = By Block, true = By Texture
    [ObservableProperty] private bool _showOriginal;

    [ObservableProperty] private PreviewTextureSet? _preview3DSet;
    [ObservableProperty] private ModelKind _previewModelKind = ModelKind.Cube;

    [ObservableProperty] private ScanResult? _scan;
    [ObservableProperty] private string _search = "";
    [ObservableProperty] private string? _selectedNamespace = "All namespaces";
    [ObservableProperty] private BlockRow? _selectedBlock;
    public ObservableCollection<BlockRow> FilteredBlocks { get; } = new();
    public ObservableCollection<BlockRow> SelectedBlocks { get; } = new();
    public ObservableCollection<string> Namespaces { get; } = new() { "All namespaces" };

    [ObservableProperty] private string _textureSearch = "";
    [ObservableProperty] private string _pattern = "{dye}_{name}.png";
    [ObservableProperty] private string _exportDir = "";

    public ObservableCollection<TextureItem> TextureItems { get; } = new();
    public ObservableCollection<BlockGroup> BlockGroups { get; } = new();
    public ObservableCollection<DyeItem> DyeItems { get; } = new();
    public ObservableCollection<CategoryItem> CategoryItems { get; } = new();
    public ObservableCollection<PreviewTile> PreviewTiles { get; } = new();
    public ObservableCollection<string> CategoryNames { get; } = new();
    public ObservableCollection<string> ExceptionTabs { get; } = new();
    public ObservableCollection<DyeCheck> CurrentDyes { get; } = new();
    public ObservableCollection<string> DyeNames { get; } = new();

    [ObservableProperty] private bool _showProjectChooser = true;
    public ObservableCollection<ProjectEntry> ProjectEntries { get; } = new();
    [ObservableProperty] private ProjectEntry? _selectedProjectEntry;

    DispatcherTimer? _tilesThrottle;
    DispatcherTimer? _saveThrottle;

    TexImage? _cachedSrc;
    string? _cachedSrcKey;

    RecolorSettings? _subscribed;

        // ── Dye / category management ────────────────────────────
    static string NormName(string s) => s.Trim().ToLowerInvariant().Replace(' ', '_');

    static string? NormHex(string s)
    {
        s = s.Trim().TrimStart('#').ToUpperInvariant();
        if (s.Length == 3) s = $"{s[0]}{s[0]}{s[1]}{s[1]}{s[2]}{s[2]}";
        if (s.Length != 6) return null;
        foreach (var c in s) if (!Uri.IsHexDigit(c)) return null;
        return "#" + s;
    }

    static Dictionary<string, T> RekeyDictionary<T>(Dictionary<string, T> src, string oldKey, string newKey)
    {
        var dst = new Dictionary<string, T>(src.Count);
        foreach (var (k, v) in src) dst[k == oldKey ? newKey : k] = v;
        return dst;
    }

    void RekeyCompensation(Func<string, string, string, (string, string, string)> fn)
    {
        var dst = new Dictionary<string, CompensationSettings>(Config.Compensation.Count);
        foreach (var (k, v) in Config.Compensation)
        {
            var parts = k.Split('|');
            if (parts.Length != 3) { dst[k] = v; continue; }
            var (tp, c, d) = fn(parts[0], parts[1], parts[2]);
            dst[$"{tp}|{c}|{d}"] = v;
        }
        Config.Compensation = dst;
    }

    [RelayCommand]
    async Task AddDye()
    {
        var owner = GetWindow(); if (owner is null) return;

        var name = await new InputDialog("Add dye", "Dye name:", "").ShowDialog<string?>(owner);
        if (string.IsNullOrWhiteSpace(name)) return;
        var norm = NormName(name);
        if (Config.Dyes.ContainsKey(norm)) { Status = $"'{norm}' already exists."; return; }

        var hex = await new InputDialog("Add dye", $"Color for '{norm}' (hex):", "#FFFFFF").ShowDialog<string?>(owner);
        if (string.IsNullOrWhiteSpace(hex)) return;
        var nh = NormHex(hex);
        if (nh is null) { Status = "Invalid hex color."; return; }

        Config.Dyes[norm] = new Dye { Color = nh };
        foreach (var cat in Config.Categories.Values)
            cat.Colors[norm] = nh;

        SaveProject();
        RefreshDyes();
        RefreshCategories();
        SelectedDye = norm;
        Status = $"Added dye '{norm}'.";
    }

    [RelayCommand]
    async Task RenameDye(DyeItem? item)
    {
        if (item is null) return;
        var owner = GetWindow(); if (owner is null) return;

        var old = item.Name;
        var name = await new InputDialog("Rename dye", $"New name for '{old}':", old).ShowDialog<string?>(owner);
        if (string.IsNullOrWhiteSpace(name)) return;
        var norm = NormName(name);
        if (norm == old) return;
        if (Config.Dyes.ContainsKey(norm)) { Status = $"'{norm}' already exists."; return; }

        Config.Dyes = RekeyDictionary(Config.Dyes, old, norm);
        foreach (var cat in Config.Categories.Values)
            cat.Colors = RekeyDictionary(cat.Colors, old, norm);
        foreach (var tex in Config.Textures.Values)
            foreach (var ex in tex.Exceptions)
                for (int i = 0; i < ex.Dyes.Count; i++)
                    if (ex.Dyes[i] == old) ex.Dyes[i] = norm;
        RekeyCompensation((tp, c, d) => (tp, c, d == old ? norm : d));

        SaveProject();
        if (SelectedDye == old) SelectedDye = norm;
        RefreshDyes();
        RefreshCategories();
        RebuildAllPreviews();
        RebuildPreview();
        Status = $"Renamed '{old}' → '{norm}'.";
    }

    [RelayCommand]
    async Task ChangeDyeColor(DyeItem? item)
    {
        if (item is null) return;
        var owner = GetWindow(); if (owner is null) return;

        var cur = Config.Dyes[item.Name].Color;
        var input = await new InputDialog("Change color", "Hex color (e.g. #80C71F):", cur).ShowDialog<string?>(owner);
        if (string.IsNullOrWhiteSpace(input)) return;
        var nh = NormHex(input);
        if (nh is null) { Status = "Invalid hex."; return; }

        Config.Dyes[item.Name].Color = nh;
        SaveProject();
        RefreshDyes();
        RefreshCategories();
        RebuildAllPreviews();
        RebuildPreview();
        Status = $"Changed '{item.Name}' to {nh}.";
    }

    [RelayCommand]
    void DeleteDye(DyeItem? item)
    {
        if (item is null) return;
        if (Config.Dyes.Count <= 1) { Status = "Need at least one dye."; return; }
        var name = item.Name;

        Config.Dyes.Remove(name);
        foreach (var cat in Config.Categories.Values)
            cat.Colors.Remove(name);
        foreach (var tex in Config.Textures.Values)
            foreach (var ex in tex.Exceptions)
                ex.Dyes.Remove(name);
        RekeyCompensation((tp, c, d) => (tp, c, d == name ? "__gone__" : d));
        Config.Compensation = Config.Compensation
            .Where(kv => !kv.Key.EndsWith("|__gone__"))
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        SaveProject();
        if (SelectedDye == name) SelectedDye = Config.Dyes.Keys.FirstOrDefault();
        RefreshDyes();
        RefreshCategories();
        RebuildAllPreviews();
        Status = $"Deleted dye '{name}'.";
    }

    [RelayCommand]
    void DuplicateDye(DyeItem? item)
    {
        if (item is null) return;
        var old = item.Name;
        var taken = Config.Dyes.Keys.ToHashSet();
        var n = $"{old}_copy";
        int i = 2; while (taken.Contains(n)) { n = $"{old}_copy_{i++}"; }

        Config.Dyes[n] = new Dye { Color = Config.Dyes[old].Color, Icon = Config.Dyes[old].Icon };
        foreach (var cat in Config.Categories.Values)
            cat.Colors[n] = cat.Colors.TryGetValue(old, out var c) ? c : Config.Dyes[old].Color;

        SaveProject();
        RefreshDyes();
        RefreshCategories();
        SelectedDye = n;
        Status = $"Duplicated '{old}' as '{n}'.";
    }

    [RelayCommand]
    async Task AddCategory()
    {
        var owner = GetWindow(); if (owner is null) return;
        var name = await new InputDialog("Add category", "Category name:", "").ShowDialog<string?>(owner);
        if (string.IsNullOrWhiteSpace(name)) return;
        var norm = NormName(name);
        if (Config.Categories.ContainsKey(norm)) { Status = $"'{norm}' already exists."; return; }

        var cat = new Category();
        foreach (var (d, dye) in Config.Dyes)
            cat.Colors[d] = dye.Color;
        Config.Categories[norm] = cat;

        SaveProject();
        RefreshCategories();
        Status = $"Added category '{norm}'.";
    }

    [RelayCommand]
    async Task RenameCategory(CategoryItem? item)
    {
        if (item is null) return;
        var owner = GetWindow(); if (owner is null) return;
        var old = item.Name;
        var name = await new InputDialog("Rename category", $"New name for '{old}':", old).ShowDialog<string?>(owner);
        if (string.IsNullOrWhiteSpace(name)) return;
        var norm = NormName(name);
        if (norm == old) return;
        if (Config.Categories.ContainsKey(norm)) { Status = $"'{norm}' already exists."; return; }

        Config.Categories = RekeyDictionary(Config.Categories, old, norm);
        foreach (var tex in Config.Textures.Values)
            if (tex.Category == old) tex.Category = norm;
        RekeyCompensation((tp, c, d) => (tp, c == old ? norm : c, d));

        SaveProject();
        RefreshCategories();
        RebuildAllPreviews();
        RebuildPreview();
        Status = $"Renamed category '{old}' → '{norm}'.";
    }

    [RelayCommand]
    async Task ChangeCategoryColor(CategoryItem? item)
    {
        if (item is null || SelectedDye is null) return;
        var owner = GetWindow(); if (owner is null) return;
        var cat = Config.Categories[item.Name];
        var cur = cat.Colors.TryGetValue(SelectedDye, out var h) ? h : Config.Dyes[SelectedDye].Color;
        var input = await new InputDialog("Change color",
            $"Hex for '{item.Name}' / {SelectedDye}:", cur).ShowDialog<string?>(owner);
        if (string.IsNullOrWhiteSpace(input)) return;
        var nh = NormHex(input);
        if (nh is null) { Status = "Invalid hex."; return; }

        cat.Colors[SelectedDye] = nh;
        SaveProject();
        RefreshCategories();
        RebuildAllPreviews();
        RebuildPreview();
        Status = $"Changed {item.Name}/{SelectedDye} to {nh}.";
    }

    [RelayCommand]
    void DeleteCategory(CategoryItem? item)
    {
        if (item is null) return;
        if (Config.Categories.Count <= 1) { Status = "Need at least one category."; return; }
        var name = item.Name;

        Config.Categories.Remove(name);
        foreach (var tex in Config.Textures.Values)
            if (tex.Category == name) tex.Category = null;
        RekeyCompensation((tp, c, d) => (tp, c == name ? "__gone__" : c, d));
        Config.Compensation = Config.Compensation
            .Where(kv => kv.Key.Split('|')[1] != "__gone__")
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        SaveProject();
        RefreshCategories();
        RebuildAllPreviews();
        RebuildPreview();
        Status = $"Deleted category '{name}'.";
    }

    [RelayCommand]
    void DuplicateCategory(CategoryItem? item)
    {
        if (item is null) return;
        var old = item.Name;
        var taken = Config.Categories.Keys.ToHashSet();
        var n = $"{old}_copy";
        int i = 2; while (taken.Contains(n)) { n = $"{old}_copy_{i++}"; }

        var src = Config.Categories[old];
        Config.Categories[n] = new Category
        {
            Icon = src.Icon,
            Colors = new Dictionary<string, string>(src.Colors),
        };

        SaveProject();
        RefreshCategories();
        Status = $"Duplicated '{old}' as '{n}'.";
    }

    public string MinecraftSummary
    {
        get
        {
            if (!string.IsNullOrEmpty(Config.Mc.ClientJarOverride))
                return "Jar: " + Path.GetFileName(Config.Mc.ClientJarOverride);
            if (!string.IsNullOrEmpty(Config.Mc.Dir))
                return "Folder: " + Config.Mc.Dir;
            return "Nothing selected — click Browse folder… or Browse jar…";
        }
    }

    public string ExtraJarsSummary =>
        Config.Mc.ExtraJars.Count == 0 ? "No extra jars" : $"{Config.Mc.ExtraJars.Count} extra jar(s)";

    public bool IsInExceptionTab => ExceptionTabIndex > 0;
    public bool CanAddException => CurrentEntry is not null && CurrentEntry.Exceptions.Count < 6;
    public bool Method2Visible => CurrentProfile?.Method2.HasValue == true;

    public MainViewModel() => RefreshProjectList();

    // ── Throttles & profile subscription ────────────────────
    void EnsureThrottles()
    {
        if (_tilesThrottle is null)
        {
            _tilesThrottle = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
            _tilesThrottle.Tick += (_, _) =>
            {
                _tilesThrottle!.Stop();
                RebuildAllPreviews();
            };
        }
        if (_saveThrottle is null)
        {
            _saveThrottle = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _saveThrottle.Tick += (_, _) =>
            {
                _saveThrottle!.Stop();
                SaveProject();
            };
        }
    }

    void SubscribeToProfile()
    {
        var cur = CurrentProfile;
        if (ReferenceEquals(_subscribed, cur)) return;
        if (_subscribed is not null) _subscribed.PropertyChanged -= OnProfileChanged;
        _subscribed = cur;
        if (_subscribed is not null) _subscribed.PropertyChanged += OnProfileChanged;
    }

    void OnProfileChanged(object? sender, PropertyChangedEventArgs e)
    {
        RebuildPreview();
        EnsureThrottles();
        _tilesThrottle!.Stop(); _tilesThrottle.Start();
        _saveThrottle!.Stop();  _saveThrottle.Start();

        if (e.PropertyName == nameof(RecolorSettings.Method) ||
            e.PropertyName == nameof(RecolorSettings.Method2))
            OnPropertyChanged(nameof(Method2Visible));
    }

    // ── Project chooser ─────────────────────────────────────
    void RefreshProjectList()
    {
        ProjectEntries.Clear();
        foreach (var name in ProjectService.ListProjects())
        {
            var f = ProjectService.ProjectFile(name);
            ProjectEntries.Add(new ProjectEntry(name, File.Exists(f) ? File.GetLastWriteTime(f) : DateTime.MinValue));
        }
    }

    [RelayCommand]
    void OpenSelectedProject()
    {
        if (SelectedProjectEntry is null) { Status = "Select a project first."; return; }
        LoadProject(SelectedProjectEntry.Name);
        ShowProjectChooser = false;
    }

    [RelayCommand]
    void NewProject()
    {
        int n = 1;
        var taken = ProjectEntries.Select(p => p.Name).ToHashSet();
        while (taken.Contains($"project_{n}")) n++;
        var name = $"project_{n}";
        ProjectService.Create(name);
        RefreshProjectList();
        LoadProject(name);
        ShowProjectChooser = false;
    }

    [RelayCommand]
    void DeleteSelectedProject()
    {
        if (SelectedProjectEntry is null) return;
        if (SelectedProjectEntry.Name == ProjectName) { Status = "Cannot delete the open project."; return; }
        try { Directory.Delete(ProjectService.ProjectDir(SelectedProjectEntry.Name), true); }
        catch (Exception ex) { Status = "Delete failed: " + ex.Message; }
        RefreshProjectList();
    }

    [RelayCommand]
    void ShowProjectChooserDialog() { RefreshProjectList(); ShowProjectChooser = true; }

    [RelayCommand]
    void CloseProjectChooser() { if (ProjectName is not null) ShowProjectChooser = false; }

    // ── Load / save ─────────────────────────────────────────
    public void LoadProject(string name)
    {
        ProjectName = name;
        Config = ProjectService.Load(name);
        Pattern = Config.Pattern;
        ExportDir = Config.ExportDir ?? "";
        _cachedSrc = null; _cachedSrcKey = null;
        RefreshDyes();
        RefreshCategories();
        RefreshTextureList();
        RefreshExceptionTabs();
        RebuildCurrentDyes();
        OnPropertyChanged(nameof(MinecraftSummary));
        OnPropertyChanged(nameof(ExtraJarsSummary));
        OnPropertyChanged(nameof(Method2Visible));
        OnPropertyChanged(nameof(CurrentProfile));
        SubscribeToProfile();
        RebuildPreview();
    }

    public void SaveProject()
    {
        if (ProjectName is null) return;
        Config.Pattern = Pattern;
        Config.ExportDir = string.IsNullOrEmpty(ExportDir) ? null : ExportDir;
        ProjectService.Save(ProjectName, Config);
    }

    Avalonia.Controls.Window? GetWindow() =>
        Avalonia.Application.Current?.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime d
                ? d.MainWindow
                : null;

    [RelayCommand] void SetStage(string s) { if (int.TryParse(s, out var n)) Stage = n; }
    [RelayCommand] void Save() => SaveProject();

    [RelayCommand]
    async Task PickDestination()
    {
        var w = GetWindow(); if (w is null) return;
        var f = await w.StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions { Title = "Export destination", AllowMultiple = false });
        if (f.Count > 0) { ExportDir = f[0].Path.LocalPath; Config.ExportDir = ExportDir; SaveProject(); }
    }

    [RelayCommand]
    void OpenProjectsFolder()
    {
        try
        {
            Directory.CreateDirectory(ProjectService.RootDir);
            Process.Start(new ProcessStartInfo { FileName = ProjectService.RootDir, UseShellExecute = true });
        }
        catch (Exception ex) { Status = "Could not open folder: " + ex.Message; }
    }

    // ── Minecraft pickers ───────────────────────────────────
    [RelayCommand]
    void AutoDetect()
    {
        var found = MinecraftLocator.FindInstall();
        if (found is null) { Status = "No Minecraft install detected."; return; }
        Config.Mc.Dir = found; Config.Mc.ClientJarOverride = null;
        OnPropertyChanged(nameof(Config));
        OnPropertyChanged(nameof(MinecraftSummary));
        SaveProject();
        Status = "Detected: " + found;
    }

    [RelayCommand]
    async Task BrowseMinecraftFolder()
    {
        var w = GetWindow(); if (w is null) return;
        var f = await w.StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            { Title = "Select Minecraft folder (contains versions/ and mods/)", AllowMultiple = false });
        if (f.Count > 0)
        {
            Config.Mc.Dir = f[0].Path.LocalPath; Config.Mc.ClientJarOverride = null;
            OnPropertyChanged(nameof(Config));
            OnPropertyChanged(nameof(MinecraftSummary));
            SaveProject();
        }
    }

    [RelayCommand]
    async Task BrowseClientJar()
    {
        var w = GetWindow(); if (w is null) return;
        var f = await w.StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = "Select Minecraft client jar",
                AllowMultiple = false,
                FileTypeFilter = new[] { new FilePickerFileType("Jar files") { Patterns = new[] { "*.jar" } } },
            });
        if (f.Count > 0)
        {
            Config.Mc.ClientJarOverride = f[0].Path.LocalPath;
            OnPropertyChanged(nameof(Config));
            OnPropertyChanged(nameof(MinecraftSummary));
            SaveProject();
        }
    }

    [RelayCommand]
    async Task AddExtraJar()
    {
        var w = GetWindow(); if (w is null) return;
        var f = await w.StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = "Add mod jars",
                AllowMultiple = true,
                FileTypeFilter = new[] { new FilePickerFileType("Jar files") { Patterns = new[] { "*.jar" } } },
            });
        int added = 0;
        foreach (var x in f)
            if (!Config.Mc.ExtraJars.Contains(x.Path.LocalPath))
            { Config.Mc.ExtraJars.Add(x.Path.LocalPath); added++; }
        OnPropertyChanged(nameof(Config));
        OnPropertyChanged(nameof(ExtraJarsSummary));
        SaveProject();
        Status = added > 0 ? $"Added {added} extra jar(s)" : "No new jars added";
    }

    [RelayCommand]
    void ClearExtraJars()
    {
        Config.Mc.ExtraJars.Clear();
        OnPropertyChanged(nameof(Config));
        OnPropertyChanged(nameof(ExtraJarsSummary));
        SaveProject();
    }

    // ── Recolor commands ────────────────────────────────────
    [RelayCommand] void SetModel(string kind) => PreviewModelKind = Enum.Parse<ModelKind>(kind, true);
    [RelayCommand] void SelectTab(string tab) => ExceptionTabIndex = ExceptionTabs.IndexOf(tab);

    [RelayCommand]
    void SetCategory(string name)
    {
        var entry = CurrentEntry;
        if (entry is null) return;
        entry.Category = name;
        SaveProject();
        RefreshCategories();
        RebuildAllPreviews();
        RebuildPreview();
    }

    [RelayCommand]
    async Task AddTextureFile()
    {
        var w = GetWindow(); if (w is null || ProjectName is null) return;
        var f = await w.StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = "Add texture image(s)",
                AllowMultiple = true,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Images") { Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.bmp" } },
                },
            });
        int added = 0;
        foreach (var x in f)
        {
            var path = x.Path.LocalPath;
            var name = Path.GetFileNameWithoutExtension(path);
            var key = "custom/" + Path.GetFileName(path);
            var disk = Path.Combine(ProjectService.ProjectDir(ProjectName), key);
            Directory.CreateDirectory(Path.GetDirectoryName(disk)!);
            File.Copy(path, disk, true);
            if (!Config.Textures.ContainsKey(key))
            {
                var taken = Config.Textures.Values.Select(v => v.Name ?? "").ToHashSet();
                Config.Textures[key] = new TextureEntry
                {
                    Name = Unique(name, taken),
                    Category = CategoryNames.FirstOrDefault(),
                };
                added++;
            }
        }
        SaveProject();
        RefreshTextureList();
        Status = $"Added {added} texture(s)";
    }

    // ── Block group / texture list commands ─────────────────
    [RelayCommand]
void SelectGroup(BlockGroup? g)
{
    if (g?.MainTexture is null) return;
    g.IsExpanded = true;                       // click-to-select expands the group
    SelectedTextureItem = g.MainTexture;
}

    [RelayCommand]
    void SelectTextureItem(TextureItem? t)
    {
        if (t is null) return;
        SelectedTextureItem = t;
    }

    [RelayCommand]
    void ToggleGroup(BlockGroup? g)
    {
        if (g is null) return;
        g.IsExpanded = !g.IsExpanded;
    }

    [RelayCommand]
    void ResetProfileToDefaults()
    {
        var entry = CurrentEntry;
        if (entry is null) return;
        var current = CurrentProfile;
        if (current is null) return;

        current.PropertyChanged -= OnProfileChanged;
        try
        {
            var savedDyes = (current as ExceptionProfile)?.Dyes.ToList();
            CopySettings(new RecolorSettings(), current);
            if (current is ExceptionProfile ex && savedDyes is not null)
            {
                ex.Dyes.Clear();
                ex.Dyes.AddRange(savedDyes);
            }
        }
        finally
        {
            current.PropertyChanged += OnProfileChanged;
        }

        SaveProject();
        RebuildAllPreviews();
        RebuildPreview();
        Status = "All sliders reset to defaults";
    }

    [RelayCommand]
    void AddException()
    {
        var entry = CurrentEntry;
        if (entry is null) return;
        if (entry.Exceptions.Count >= 6) { Status = "Maximum of 6 exceptions reached."; return; }
        var ex = new ExceptionProfile();
        CopySettings(entry.Settings, ex);
        entry.Exceptions.Add(ex);
        RefreshExceptionTabs();
        ExceptionTabIndex = entry.Exceptions.Count;
        RebuildCurrentDyes();
        OnPropertyChanged(nameof(CanAddException));
        OnPropertyChanged(nameof(IsInExceptionTab));
        OnPropertyChanged(nameof(CurrentProfile));
        SaveProject();
        SubscribeToProfile();
        RebuildPreview();
        Status = $"Added Exception {entry.Exceptions.Count}";
    }

    [RelayCommand]
    void DeleteException()
    {
        var entry = CurrentEntry;
        if (entry is null || ExceptionTabIndex <= 0 || ExceptionTabIndex > entry.Exceptions.Count) return;
        entry.Exceptions.RemoveAt(ExceptionTabIndex - 1);
        ExceptionTabIndex = 0;
        RefreshExceptionTabs();
        RebuildCurrentDyes();
        OnPropertyChanged(nameof(CanAddException));
        OnPropertyChanged(nameof(IsInExceptionTab));
        OnPropertyChanged(nameof(CurrentProfile));
        SaveProject();
        SubscribeToProfile();
        RebuildPreview();
        Status = "Exception deleted";
    }

    [RelayCommand]
    void ResetException()
    {
        var entry = CurrentEntry;
        if (entry is null || ExceptionTabIndex <= 0 || ExceptionTabIndex > entry.Exceptions.Count) return;
        var ex = entry.Exceptions[ExceptionTabIndex - 1];
        var dyes = ex.Dyes.ToList();
        CopySettings(entry.Settings, ex);
        ex.Dyes.Clear();
        ex.Dyes.AddRange(dyes);
        SaveProject();
        RebuildPreview();
        Status = "Exception reset to the default profile's settings";
    }

    static void CopySettings(RecolorSettings src, RecolorSettings dst)
    {
        dst.Method = src.Method; dst.Method2 = src.Method2; dst.BlendPct = src.BlendPct;
        dst.Tint = src.Tint; dst.Sat = src.Sat;
        dst.FlattenHue = src.FlattenHue; dst.FlattenSat = src.FlattenSat; dst.FlattenVal = src.FlattenVal;
        dst.HueSplit = src.HueSplit; dst.ShadingGain = src.ShadingGain;
        dst.ShadowSat = src.ShadowSat; dst.ExtremeComp = src.ExtremeComp;
        dst.BrightShadowDark = src.BrightShadowDark; dst.Tolerance = src.Tolerance;
        dst.ReferencePixel = src.ReferencePixel?.ToArray();
        dst.Tint2 = src.Tint2; dst.Sat2 = src.Sat2;
        dst.FlattenHue2 = src.FlattenHue2; dst.FlattenSat2 = src.FlattenSat2; dst.FlattenVal2 = src.FlattenVal2;
        dst.HueSplit2 = src.HueSplit2; dst.ShadingGain2 = src.ShadingGain2;
        dst.ShadowSat2 = src.ShadowSat2; dst.ExtremeComp2 = src.ExtremeComp2;
        dst.BrightShadowDark2 = src.BrightShadowDark2; dst.Tolerance2 = src.Tolerance2;

        dst.Pre.Brightness = src.Pre.Brightness;
        dst.Pre.Contrast = src.Pre.Contrast;
        dst.Pre.Vibrance = src.Pre.Vibrance;
        dst.Pre.Clarity = src.Pre.Clarity;
        dst.Pre.HueShift = src.Pre.HueShift;

        dst.Post.Brightness = src.Post.Brightness;
        dst.Post.Contrast = src.Post.Contrast;
        dst.Post.Vibrance = src.Post.Vibrance;
        dst.Post.Clarity = src.Post.Clarity;
        dst.Post.HueShift = src.Post.HueShift;
    }

    void RebuildCurrentDyes()
    {
        foreach (var c in CurrentDyes) c.Changed -= OnDyeCheckChanged;
        CurrentDyes.Clear();
        var entry = CurrentEntry;
        if (entry is null) return;
        HashSet<string> members = (ExceptionTabIndex > 0 && ExceptionTabIndex <= entry.Exceptions.Count)
            ? entry.Exceptions[ExceptionTabIndex - 1].Dyes.ToHashSet() : new HashSet<string>();
        foreach (var dye in DyeNames)
        {
            var c = new DyeCheck(dye, members.Contains(dye));
            c.Changed += OnDyeCheckChanged;
            CurrentDyes.Add(c);
        }
    }

    void OnDyeCheckChanged(object? sender, EventArgs e)
    {
        if (sender is not DyeCheck c) return;
        var entry = CurrentEntry;
        if (entry is null || ExceptionTabIndex <= 0 || ExceptionTabIndex > entry.Exceptions.Count) return;
        foreach (var ex in entry.Exceptions) ex.Dyes.Remove(c.Name);
        if (c.IsChecked) entry.Exceptions[ExceptionTabIndex - 1].Dyes.Add(c.Name);
        SaveProject();
        RebuildAllPreviews();
        RebuildPreview();
    }

    public void RefreshDyes()
    {
        DyeNames.Clear(); DyeItems.Clear();
        foreach (var (name, dye) in Config.Dyes)
        {
            DyeNames.Add(name);
            DyeItems.Add(new DyeItem(name, dye.Color));
        }
        if (SelectedDye is null && DyeNames.Count > 0) SelectedDye = DyeNames[0];
        SelectedDyeItem = DyeItems.FirstOrDefault(d => d.Name == SelectedDye);
        RebuildCurrentDyes();
    }

    public void RefreshCategories()
    {
        CategoryNames.Clear(); CategoryItems.Clear();
        foreach (var (name, category) in Config.Categories)
        {
            CategoryNames.Add(name);
            var hex = SelectedDye is not null && category.Colors.TryGetValue(SelectedDye, out var h)
                ? h : Config.Dyes.TryGetValue(SelectedDye ?? "", out var d) ? d.Color : "#808080";
            CategoryItems.Add(new CategoryItem(name, hex));
        }
    }

    // ── Stage 1 ─────────────────────────────────────────────
    [RelayCommand]
    async Task ScanAsync()
    {
        Status = "Scanning…";
        try
        {
            string? jar = !string.IsNullOrEmpty(Config.Mc.ClientJarOverride) &&
                          File.Exists(Config.Mc.ClientJarOverride)
                ? Config.Mc.ClientJarOverride
                : MinecraftLocator.FindClientJar(Config.Mc.Dir, Config.Mc.Version);
            if (jar is null && string.IsNullOrEmpty(Config.Mc.Dir) && Config.Mc.ExtraJars.Count == 0)
            { Status = "Pick a Minecraft folder, a client jar, or add a mod jar first."; return; }
            Scan = await JarScanner.ScanAsync(jar, Config.Mc.Dir, Config.Mc.IncludeMods, Config.Mc.ExtraJars,
                msg => Dispatcher.UIThread.Post(() => Status = msg));
            Namespaces.Clear(); Namespaces.Add("All namespaces");
            foreach (var ns in Scan.Blocks.Keys.Select(k => k.Split(':')[0]).Distinct().OrderBy(x => x))
                Namespaces.Add(ns);
            ApplyFilter();
            Status = $"{Scan.Blocks.Count} blocks from {Scan.Sources.Count} sources";
        }
        catch (Exception ex) { Status = "Scan failed: " + ex.Message; }
    }

    [RelayCommand]
    public void ApplyFilter()
    {
        FilteredBlocks.Clear();
        if (Scan is null) return;
        var q = Search.ToLowerInvariant();
        var ns = SelectedNamespace?.Split(' ')[0] ?? "All namespaces";
        foreach (var (bid, info) in Scan.Blocks)
        {
            if (ns != "All namespaces" && !bid.StartsWith(ns + ":")) continue;
            if (!string.IsNullOrEmpty(q) &&
                !bid.ToLowerInvariant().Contains(q) &&
                !info.Name.ToLowerInvariant().Contains(q)) continue;
            FilteredBlocks.Add(new BlockRow(bid, info));
            if (FilteredBlocks.Count >= 8000) break;
        }
    }

    [RelayCommand] void SelectAllShown() { foreach (var r in FilteredBlocks) r.IsSelected = true; }
    [RelayCommand] void DeselectAll() { foreach (var r in FilteredBlocks) r.IsSelected = false; }

    [RelayCommand]
    void AddSelectedBlocks()
    {
        var byCheck = FilteredBlocks.Where(r => r.IsSelected).ToList();
        if (byCheck.Count > 0) { AddBlockRows(byCheck); return; }
        var byHighlight = SelectedBlocks.Count > 0
            ? SelectedBlocks.ToArray()
            : (SelectedBlock is not null ? new[] { SelectedBlock } : Array.Empty<BlockRow>());
        if (byHighlight.Length == 0) { Status = "Check or highlight at least one block first."; return; }
        AddBlockRows(byHighlight);
    }

    void AddBlockRows(IReadOnlyList<BlockRow> rows)
    {
        if (rows.Count == 0 || ProjectName is null) return;
        int added = 0;
        foreach (var row in rows)
        {
            Config.Blocks[row.Id] = row.Info;
            foreach (var (slot, tid) in row.Info.Slots)
            {
                string key = CacheKeyFor(tid);
                var disk = Path.Combine(ProjectService.ProjectDir(ProjectName), key);
                if (!File.Exists(disk) && Scan is not null)
                {
                    var bytes = JarScanner.GetTextureBytes(Scan, tid);
                    if (bytes is not null)
                    {
                        var dir = Path.GetDirectoryName(disk);
                        if (dir is not null) Directory.CreateDirectory(dir);
                        File.WriteAllBytes(disk, bytes);
                    }
                }
                if (!Config.Textures.TryGetValue(key, out var entry))
                {
                    var baseName = tid.Split('/').Last();
                    if (row.Info.Family is not null && row.Info.Color is not null &&
                        baseName.StartsWith(row.Info.Color + "_"))
                        baseName = baseName[(row.Info.Color.Length + 1)..];
                    var taken = Config.Textures.Values.Select(v => v.Name ?? "").ToHashSet();
                    entry = new TextureEntry
                    {
                        Name = Unique(baseName, taken),
                        Category = CategoryNames.FirstOrDefault(),
                        TexId = tid,
                    };
                    Config.Textures[key] = entry;
                    added++;
                }
                if (!entry.Blocks.Contains(row.Id)) entry.Blocks.Add(row.Id);
            }
            row.IsSelected = false;
        }
        SaveProject();
        RefreshTextureList();
        Status = $"Added {rows.Count} block(s), {added} new texture(s)";
        if (Config.Textures.Count > 0) Stage = 2;
    }

    static string CacheKeyFor(string tid) => "cache/" + tid.Replace(':', '/') + ".png";
    static string Unique(string name, HashSet<string> taken)
    {
        if (!taken.Contains(name)) return name;
        for (int i = 2; ; i++) if (!taken.Contains($"{name}_{i}")) return $"{name}_{i}";
    }

    // ── Stage 2: texture/block list ─────────────────────────
    public void RefreshTextureList()
    {
        TextureItems.Clear();
        BlockGroups.Clear();
        if (ProjectName is null) return;

        var q = TextureSearch?.Trim().ToLowerInvariant() ?? "";
        var groups = new Dictionary<string, BlockGroup>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, entry) in Config.Textures)
        {
            var name = entry.Name ?? key;
            if (q.Length > 0 && !name.ToLowerInvariant().Contains(q)) continue;

            var (groupKey, groupName) = FindBestOwnerBlock(entry);
            if (string.IsNullOrEmpty(groupKey))
            {
                groupKey = "__ungrouped__";
                groupName = "Ungrouped";
            }

            Bitmap? thumb = null;
            try
            {
                var p = Path.Combine(ProjectService.ProjectDir(ProjectName), key);
                if (File.Exists(p)) thumb = new Bitmap(p);
            }
            catch { }

            var item = new TextureItem(key, name, thumb, entry)
            {
                GroupKey = groupKey!,
                GroupName = groupName ?? groupKey!,
            };
            TextureItems.Add(item);

            if (!groups.TryGetValue(groupKey!, out var g))
            {
                g = new BlockGroup(groupKey!, groupName ?? groupKey!);
                groups[groupKey!] = g;
            }
            g.Items.Add(item);
        }

        // Second pass: sort, compute representative thumbnails, register groups.
        foreach (var g in groups.Values.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase))
        {
            g.RepresentativeThumbnail = g.MainTexture?.Thumbnail ?? g.Items.FirstOrDefault()?.Thumbnail;
            BlockGroups.Add(g);
        }

        OnPropertyChanged(nameof(TextureItems));
        OnPropertyChanged(nameof(BlockGroups));
    }

    (string? key, string? name) FindBestOwnerBlock(TextureEntry entry)
    {
        if (entry.Blocks.Count == 0) return (null, null);

        var entryName = (entry.Name ?? "").ToLowerInvariant();
        if (entryName.Length == 0) return (null, null);

        string? bestId = null;
        int bestScore = int.MinValue;

        foreach (var bid in entry.Blocks)
        {
            if (!Config.Blocks.ContainsKey(bid)) continue;
            int score = ScoreBlockForTexture(bid, entryName);
            if (score > bestScore) { bestScore = score; bestId = bid; }
        }

        if (bestId is null) return (null, null);
        Config.Blocks.TryGetValue(bestId, out var block);
        return (bestId, block?.Name ?? bestId.Split(':').Last());
    }

    static int ScoreBlockForTexture(string blockId, string entryName)
    {
        if (string.IsNullOrEmpty(entryName)) return 0;
        var path = blockId.Split(':').LastOrDefault()?.ToLowerInvariant() ?? "";
        int score = 0;

        if (path == entryName)                                              score += 200;
        else if (path.StartsWith(entryName) || path.EndsWith(entryName))    score += 100;
        else if (path.Contains(entryName) || entryName.Contains(path))      score += 50;

        if (path.Contains("frame"))                                    score -= 80;
        if (path.Contains("item") && !path.Contains("block"))          score -= 30;
        if (path.Contains("potted"))                                   score -= 20;
        if (path.Contains("wall_torch") || path.Contains("torch"))     score -= 20;
        if (path.Contains("sign") && !entryName.Contains("sign"))      score -= 20;

        return score;
    }

    public void RefreshExceptionTabs()
    {
        ExceptionTabs.Clear();
        ExceptionTabs.Add("default");
        var entry = CurrentEntry;
        if (entry != null)
            for (int i = 0; i < entry.Exceptions.Count; i++)
                ExceptionTabs.Add($"★ Ex {i + 1}");
        if (ExceptionTabIndex >= ExceptionTabs.Count)
            ExceptionTabIndex = ExceptionTabs.Count - 1;
    }

    public TextureEntry? CurrentEntry => SelectedTexture;

    public string? CurrentTextureKey =>
        SelectedTexture is null ? null
        : Config.Textures.FirstOrDefault(kv => ReferenceEquals(kv.Value, SelectedTexture)).Key;

    public RecolorSettings? CurrentProfile
    {
        get
        {
            var e = CurrentEntry;
            if (e == null) return null;
            if (ExceptionTabIndex > 0 && ExceptionTabIndex <= e.Exceptions.Count)
                return e.Exceptions[ExceptionTabIndex - 1];
            return e.Settings;
        }
    }

    // ── Selection & view-mode handlers ──────────────────────
    partial void OnTextureSearchChanged(string value) => RefreshTextureList();

    partial void OnSelectedTextureItemChanged(TextureItem? value)
    {
        // Update highlight flags on both views.
        foreach (var g in BlockGroups)
        {
            g.IsSelected = value is not null && g.Items.Contains(value);
            foreach (var it in g.Items)
                it.IsSelected = ReferenceEquals(it, value);
        }
        SelectedTexture = value?.Entry;
    }

    partial void OnSelectedTextureChanged(TextureEntry? value)
    {
        _cachedSrc = null; _cachedSrcKey = null;
        RefreshExceptionTabs();
        ExceptionTabIndex = 0;
        RebuildCurrentDyes();
        RefreshCategories();
        OnPropertyChanged(nameof(CanAddException));
        OnPropertyChanged(nameof(IsInExceptionTab));
        OnPropertyChanged(nameof(Method2Visible));
        OnPropertyChanged(nameof(CurrentProfile));
        SubscribeToProfile();
        RebuildAllPreviews();
        RebuildPreview();
    }

    partial void OnSelectedDyeItemChanged(DyeItem? value) { if (value is not null) SelectedDye = value.Name; }

    partial void OnSelectedDyeChanged(string? value)
    {
        RefreshCategories();
        SyncSelectedTileFromDye();
        RebuildPreview();
    }

    partial void OnSelectedPreviewTileChanged(PreviewTile? value)
    {
        if (value is not null && value.DyeName != SelectedDye) SelectedDye = value.DyeName;
    }

    partial void OnShowOriginalChanged(bool value) => RebuildPreview();

    void SyncSelectedTileFromDye()
    {
        if (SelectedDye is null) { SelectedPreviewTile = null; return; }
        SelectedPreviewTile = PreviewTiles.FirstOrDefault(t => t.DyeName == SelectedDye);
    }

    partial void OnExceptionTabIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsInExceptionTab));
        OnPropertyChanged(nameof(Method2Visible));
        OnPropertyChanged(nameof(CurrentProfile));
        RebuildCurrentDyes();
        SubscribeToProfile();
        RebuildPreview();
    }

    partial void OnPreviewModelKindChanged(ModelKind value)
    {
        if (CurrentEntry is not null) CurrentEntry.ModelKind = value;
        SaveProject();
    }

    // ── Stage 2: dye tiles ──────────────────────────────────
    public void RebuildAllPreviews()
    {
        PreviewTiles.Clear();
        var entry = CurrentEntry;
        if (entry is null || ProjectName is null) return;
        var key = CurrentTextureKey;
        if (key is null) return;
        var cat = entry.Category ?? CategoryNames.FirstOrDefault();
        if (cat is null || !Config.Categories.TryGetValue(cat, out var category)) return;

        TexImage src;
        try { src = GetSourceImage(key); }
        catch { return; }
        if (src.Width <= 0 || src.Height <= 0) return;

        foreach (var dyeName in DyeNames)
        {
            try
            {
                var profile = RenderPipeline.ProfileFor(entry, dyeName);
                var hex = category.Colors.TryGetValue(dyeName, out var h) ? h : Config.Dyes[dyeName].Color;
                var color = ColorMath.HexToRgb(hex);
                Config.Compensation.TryGetValue($"{key}|{cat}|{dyeName}", out var comp);
                var outImg = RenderPipeline.Render(src, color, profile, comp);
                if (outImg.Width <= 0 || outImg.Height <= 0) continue;
                var bmp = outImg.ToBitmap();
                bool inEx = entry.Exceptions.Any(ex => ex.Dyes.Contains(dyeName));
                PreviewTiles.Add(new PreviewTile(dyeName, bmp, inEx));
            }
            catch { }
        }
        SyncSelectedTileFromDye();
    }

    // ── Stage 2: 3D preview ─────────────────────────────────
    public void RebuildPreview()
    {
        var entry = CurrentEntry;
        if (entry is null || SelectedDye is null) { Preview3DSet = null; return; }

        var profile = CurrentProfile;
        if (profile is null) { Preview3DSet = null; return; }

        var cat = entry.Category ?? CategoryNames.FirstOrDefault();
        if (cat is null || !Config.Categories.TryGetValue(cat, out var category)) return;

        var hex = category.Colors.TryGetValue(SelectedDye, out var h) ? h : Config.Dyes[SelectedDye].Color;
        var color = ColorMath.HexToRgb(hex);

        var key = CurrentTextureKey;
        if (key is null) { Preview3DSet = null; return; }

        try
        {
            var (ownerId, _) = FindBestOwnerBlock(entry);
            var ownerBlock = ownerId is not null && Config.Blocks.TryGetValue(ownerId, out var ob) ? ob : null;

            var set = new PreviewTextureSet();

            if (ownerBlock is null)
            {
                set.All = ShowOriginal ? LoadRaw(key)
                                       : RenderOneTexture(key, profile, color, cat, null);
            }
            else
            {
                foreach (var (slot, tid) in ownerBlock.Slots)
                {
                    var slotKey = CacheKeyFor(tid);
                    var bmp = ShowOriginal
                        ? LoadRaw(slotKey)
                        : RenderSlot(slotKey, tid, profile, color, cat);

                    switch (slot)
                    {
                        case "all":      set.All    ??= bmp; break;
                        case "top":      set.Top    ??= bmp; break;
                        case "up":       set.Top    ??= bmp; break;
                        case "bottom":   set.Bottom ??= bmp; break;
                        case "down":     set.Bottom ??= bmp; break;
                        case "side":     set.Side   ??= bmp; break;
                        case "north":    set.Front  ??= bmp; break;
                        case "south":    set.Back   ??= bmp; break;
                        case "east":     set.Right  ??= bmp; break;
                        case "west":     set.Left   ??= bmp; break;
                        case "front":    set.Front  ??= bmp; break;
                        case "back":     set.Back   ??= bmp; break;
                        case "planks":   set.Side   ??= bmp; break;
                        case "end":
                            set.Top    ??= bmp;
                            set.Bottom ??= bmp;
                            break;
                        case "cross":    set.Cross  ??= bmp; break;
                        case "particle": break;
                        default:         set.All    ??= bmp; break;
                    }
                }
            }

            Preview3DSet = set;

            bool isCross =
                ownerBlock?.ModelShape == "Cross" ||
                (set.Cross is not null &&
                 set.All is null && set.Top is null && set.Bottom is null &&
                 set.Side is null && set.Front is null && set.Back is null &&
                 set.Left is null && set.Right is null);

            PreviewModelKind = isCross ? ModelKind.Cross : ModelKind.Cube;
        }
        catch (Exception ex) { Status = "Preview failed: " + ex.Message; }
    }

    Bitmap? RenderOneTexture(string key, RecolorSettings profile, Vector3 color, string cat,
                             CompensationSettings? comp)
    {
        try
        {
            var src = GetSourceImage(key);
            var outImg = RenderPipeline.Render(src, color, profile, comp);
            return outImg.ToBitmap();
        }
        catch { return null; }
    }

    Bitmap? RenderSlot(string slotKey, string slotTid, RecolorSettings currentProfile,
                       Vector3 color, string cat)
    {
        if (Config.Textures.TryGetValue(slotKey, out var sibling))
        {
            var profile = ReferenceEquals(sibling, CurrentEntry)
                ? currentProfile
                : sibling.Settings;
            Config.Compensation.TryGetValue($"{slotKey}|{cat}|{SelectedDye}", out var scomp);
            return RenderOneTexture(slotKey, profile, color, cat, scomp);
        }
        return RenderOneTexture(slotKey, currentProfile, color, cat, null);
    }

    Bitmap? LoadRaw(string key)
    {
        try
        {
            var p = Path.Combine(ProjectService.ProjectDir(ProjectName!), key);
            if (!File.Exists(p)) return null;
            using var b = new Bitmap(p);
            var img = TexImage.FromBitmap(b);
            return img.ToBitmap();
        }
        catch { return null; }
    }

    TexImage GetSourceImage(string key)
    {
        if (_cachedSrc is not null && _cachedSrcKey == key) return _cachedSrc;
        _cachedSrc = LoadTexImage(key);
        _cachedSrcKey = key;
        return _cachedSrc;
    }

    TexImage LoadTexImage(string key)
    {
        var path = Path.Combine(ProjectService.ProjectDir(ProjectName!), key);
        using var bmp = new Bitmap(path);
        return TexImage.FromBitmap(bmp);
    }

    // ── Stage 3 ─────────────────────────────────────────────
    [RelayCommand]
    async Task ExportAsync()
    {
        if (string.IsNullOrEmpty(Config.ExportDir)) { Status = "Select an export destination."; return; }
        Status = "Exporting…";
        var progress = new Progress<(int done, int total, string file)>(
            p => Status = $"{p.done}/{p.total}  {p.file}");
        try
        {
            int n = await ExportService.ExportAllAsync(Config, LoadTexImage, progress);
            Status = $"Exported {n} textures.";
        }
        catch (Exception ex) { Status = "Export failed: " + ex.Message; }
    }
}

public sealed partial class BlockRow : ObservableObject
{
    public string Id { get; }
    public BlockInfo Info { get; }
    public string TextureNames { get; }
    public string Notes { get; }
    [ObservableProperty] private bool _isSelected;

    public BlockRow(string id, BlockInfo info)
    {
        Id = id; Info = info;
        TextureNames = string.Join(", ", info.Slots.Values.Select(v => v.Split(':').Last().Split('/').Last()));
        Notes = string.Join(", ", info.Flags);
    }
}