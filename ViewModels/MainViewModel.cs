using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TextureTinter.Engine;
using TextureTinter.Minecraft;
using TextureTinter.Models;
using TextureTinter.Services;

namespace TextureTinter.ViewModels;

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

    [ObservableProperty] private Bitmap? _preview3DBitmap;
    [ObservableProperty] private ModelKind _previewModelKind = ModelKind.Cube;

    [ObservableProperty] private ScanResult? _scan;
    [ObservableProperty] private string _search = "";
    [ObservableProperty] private string? _selectedNamespace = "All namespaces";
    [ObservableProperty] private BlockRow? _selectedBlock;
    public ObservableCollection<BlockRow> FilteredBlocks { get; } = new();
    public ObservableCollection<BlockRow> SelectedBlocks { get; } = new();
    public ObservableCollection<string> Namespaces { get; } = new() { "All namespaces" };

    [ObservableProperty] private string _pattern = "{dye}_{name}.png";
    [ObservableProperty] private string _exportDir = "";

    public ObservableCollection<TextureItem> TextureItems { get; } = new();
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

    // ── Profile subscription ─────────────────────────────────
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
        _saveThrottle!.Stop(); _saveThrottle.Start();

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

    // ── Minecraft source pickers ─────────────────────────────
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

    // ── Recolor commands ─────────────────────────────────────
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

    // ── THE NEW ONE ─────────────────────────────────────────
    /// <summary>Reset every slider in the currently-edited profile (default or exception) to factory defaults.
    /// Keeps the exception's dye membership so the exception stays functional.</summary>
    [RelayCommand]
    void ResetProfileToDefaults()
    {
        var entry = CurrentEntry;
        if (entry is null) return;
        var current = CurrentProfile;
        if (current is null) return;

        // Detach so we don't render 25 times while resetting 25 fields.
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

        // Mutate Pre/Post in-place. Replacing the objects (dst.Pre = src.Pre.Clone())
        // breaks the live TwoWay bindings used by the Adjustments tab.
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

    // ── Stage 1 ──────────────────────────────────────────────
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

    // ── Stage 2 ──────────────────────────────────────────────
    public void RefreshTextureList()
    {
        TextureItems.Clear();
        if (ProjectName is null) return;
        foreach (var (key, entry) in Config.Textures)
        {
            Bitmap? thumb = null;
            try
            {
                var p = Path.Combine(ProjectService.ProjectDir(ProjectName), key);
                if (File.Exists(p)) thumb = new Bitmap(p);
            }
            catch { }
            TextureItems.Add(new TextureItem(key, entry.Name ?? key, thumb, entry));
        }
        OnPropertyChanged(nameof(TextureItems));
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

    partial void OnSelectedTextureItemChanged(TextureItem? value) => SelectedTexture = value?.Entry;

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

    public void RebuildPreview()
    {
        var entry = CurrentEntry;
        if (entry is null || SelectedDye is null) { Preview3DBitmap = null; return; }
        var profile = CurrentProfile;
        if (profile is null) { Preview3DBitmap = null; return; }
        var cat = entry.Category ?? CategoryNames.FirstOrDefault();
        if (cat is null || !Config.Categories.TryGetValue(cat, out var category)) return;
        var hex = category.Colors.TryGetValue(SelectedDye, out var h) ? h : Config.Dyes[SelectedDye].Color;
        var color = ColorMath.HexToRgb(hex);
        var key = CurrentTextureKey;
        if (key is null) { Preview3DBitmap = null; return; }

        try
        {
            Config.Compensation.TryGetValue($"{key}|{cat}|{SelectedDye}", out var comp);
            var src = GetSourceImage(key);
            var outImg = RenderPipeline.Render(src, color, profile, comp);
            Preview3DBitmap = outImg.ToBitmap();
        }
        catch (Exception ex) { Status = "Preview failed: " + ex.Message; }

        PreviewModelKind = entry.ModelKind;
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

    // ── Stage 3 ──────────────────────────────────────────────
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