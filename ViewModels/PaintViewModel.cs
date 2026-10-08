using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using McTextureTweaker.Engine;
using McTextureTweaker.Models;

namespace McTextureTweaker.ViewModels;

public partial class PaintViewModel : ObservableObject
{
    readonly MainViewModel _main;

    [ObservableProperty] private TextureEntry? _texture;
    [ObservableProperty] private string? _textureKey;
    [ObservableProperty] private PaintProject? _project;
    [ObservableProperty] private int _activeLayerIndex;
    [ObservableProperty] private PaintTool _tool = PaintTool.Pencil;
    [ObservableProperty] private int _zoom = 28;
    [ObservableProperty] private bool _showGrid = true;
    [ObservableProperty] private bool _baseIsRecolored = true;
    [ObservableProperty] private byte[]? _basePixels;
    [ObservableProperty] private Color _currentColor = Colors.White;
    [ObservableProperty] private string _hexInput = "#FFFFFF";
    [ObservableProperty] private string _newLayerName = "";

    // ── Display helpers ─────────────────────────────────────
    public string TextureName => Texture?.Name ?? "No texture selected";
    public string CoordinateText => "16 × 16";

    public float CurrentLayerOpacity
    {
        get => CurrentLayer?.Opacity ?? 1f;
        set
        {
            var l = CurrentLayer;
            if (l is null) return;
            if (Math.Abs(l.Opacity - value) < 0.0001f) return;
            l.Opacity = value;
            OnPropertyChanged();
            _main.SaveProject();
        }
    }

    public ObservableCollection<PaintLayer> Layers { get; } = new();
    public ObservableCollection<Color> Palette { get; } = new();
    public ObservableCollection<Color> CustomPalette { get; } = new();
    public ObservableCollection<Color> RecentColors { get; } = new();
    const int RecentLimit = 12;

    readonly Stack<(int, byte[])> _undo = new();
    readonly Stack<(int, byte[])> _redo = new();
    const int UndoLimit = 40;

    public PaintViewModel(MainViewModel main)
    {
        _main = main;
        SeedPalette();
        _main.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.SelectedTexture))
                OnSelectedTextureChanged();
            else if (e.PropertyName == nameof(MainViewModel.SelectedDye))
                RefreshBasePixels();
        };
    }

    void SeedPalette()
    {
        Palette.Clear();
        string[] baseColors =
        {
            "#FFFFFF", "#C0C0C0", "#808080", "#404040", "#000000",
            "#FF0000", "#FF8000", "#FFFF00", "#80C71F", "#00BB77",
            "#00FFFF", "#3AB3DA", "#3C44AA", "#8932B8", "#C74EBD",
            "#F38BAA", "#835432", "#5E7C16", "#169C9C", "#B02E26",
        };
        foreach (var h in baseColors)
            Palette.Add(ParseHexOrDefault(h, Colors.Magenta));
    }

    // ── Texture lifecycle ──────────────────────────────────
    void OnSelectedTextureChanged()
    {
        Texture = _main.SelectedTexture;
        TextureKey = _main.CurrentTextureKey;

        if (Texture is null)
        {
            Project = null;
            Layers.Clear();
            BasePixels = null;
            return;
        }

        Project = Texture.Paint ??= new PaintProject();

        Layers.Clear();
        if (Project.Layers.Count == 0)
            Project.Layers.Add(new PaintLayer { Name = "Layer 1" });
        foreach (var l in Project.Layers) Layers.Add(l);

        ActiveLayerIndex = Math.Clamp(Project.ActiveLayerIndex, 0, Math.Max(0, Layers.Count - 1));
        BaseIsRecolored = Project.BaseIsRecolored;

        CustomPalette.Clear();
        foreach (var h in Project.CustomPalette)
            CustomPalette.Add(ParseHexOrDefault(h, Colors.Gray));

        _undo.Clear();
        _redo.Clear();
        RefreshBasePixels();
    }

    void RefreshBasePixels()
    {
        BasePixels = null;
        if (Texture is null || TextureKey is null) return;
        if (_main.SelectedDye is null) return;

        var entry = Texture;
        var cat = entry.Category ?? _main.CategoryNames.FirstOrDefault();
        if (cat is null || !_main.Config.Categories.TryGetValue(cat, out var category)) return;

        try
        {
            var src = _main.GetSourceImage(TextureKey);

            TexImage img;
            if (BaseIsRecolored)
            {
                var profile = _main.CurrentProfile;
                if (profile is null) return;
                var hex = category.Colors.TryGetValue(_main.SelectedDye, out var h)
                    ? h : _main.Config.Dyes[_main.SelectedDye].Color;
                var color = ColorMath.HexToRgb(hex);
                _main.Config.Compensation.TryGetValue(
                    $"{TextureKey}|{cat}|{_main.SelectedDye}", out var comp);
                img = RenderPipeline.Render(src, color, profile, comp);
            }
            else
            {
                img = src.Clone();
            }

            BasePixels = ToBytes(img);
        }
        catch { BasePixels = null; }
    }

    static byte[] ToBytes(TexImage img)
    {
        var b = new byte[PaintLayer.ByteCount];
        for (int i = 0; i < PaintLayer.Size * PaintLayer.Size; i++)
        {
            int s = i * 4;
            b[s + 0] = Clamp(img.Data[s + 0]);
            b[s + 1] = Clamp(img.Data[s + 1]);
            b[s + 2] = Clamp(img.Data[s + 2]);
            b[s + 3] = Clamp(img.Data[s + 3]);
        }
        return b;

        static byte Clamp(float v) => v < 0 ? (byte)0 : v > 255 ? (byte)255 : (byte)(v + 0.5f);
    }

    partial void OnBaseIsRecoloredChanged(bool value)
    {
        if (Project is not null) Project.BaseIsRecolored = value;
        RefreshBasePixels();
    }

    partial void OnActiveLayerIndexChanged(int value)
    {
        if (Project is not null) Project.ActiveLayerIndex = value;
        OnPropertyChanged(nameof(CurrentLayerOpacity));
    }

    partial void OnTextureChanged(TextureEntry? value)
    {
        if (value is not null) value.Paint = Project;
        OnPropertyChanged(nameof(TextureName));
        _main.SaveProject();
    }

    // ── Tools ──────────────────────────────────────────────
    [RelayCommand] void SetPencil() => Tool = PaintTool.Pencil;
    [RelayCommand] void SetEraser() => Tool = PaintTool.Eraser;
    [RelayCommand] void SetFill() => Tool = PaintTool.Fill;
    [RelayCommand] void SetEyedropper() => Tool = PaintTool.Eyedropper;
    [RelayCommand] void SetLine() => Tool = PaintTool.Line;
    [RelayCommand] void SetRect() => Tool = PaintTool.Rect;
    [RelayCommand] void SetRectFill() => Tool = PaintTool.RectFill;
    [RelayCommand] void SetEllipse() => Tool = PaintTool.Ellipse;
    [RelayCommand] void SetEllipseFill() => Tool = PaintTool.EllipseFill;

    // ── Canvas events ──────────────────────────────────────
    public void HandleStrokeBegin()
    {
        var layer = CurrentLayer;
        if (layer is null) return;
        _undo.Push((ActiveLayerIndex, layer.PixelData));
        while (_undo.Count > UndoLimit)
        {
            var arr = _undo.ToArray();
            _undo.Clear();
            foreach (var item in arr.Take(UndoLimit).Reverse())
                _undo.Push(item);
        }
        _redo.Clear();
    }

    public void HandleColorPicked(Color c)
    {
        CurrentColor = c;
        HexInput = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        PushRecent(c);
    }

    PaintLayer? CurrentLayer =>
        ActiveLayerIndex >= 0 && ActiveLayerIndex < Layers.Count
            ? Layers[ActiveLayerIndex] : null;

    [RelayCommand]
    void Undo()
    {
        if (_undo.Count == 0) return;
        var (idx, pixels) = _undo.Pop();
        if (idx >= 0 && idx < Layers.Count)
        {
            _redo.Push((idx, Layers[idx].PixelData));
            Layers[idx].SetPixels(pixels);
        }
    }

    [RelayCommand]
    void Redo()
    {
        if (_redo.Count == 0) return;
        var (idx, pixels) = _redo.Pop();
        if (idx >= 0 && idx < Layers.Count)
        {
            _undo.Push((idx, Layers[idx].PixelData));
            Layers[idx].SetPixels(pixels);
        }
    }

    // ── Layers ─────────────────────────────────────────────
    [RelayCommand]
    void AddLayer()
    {
        if (Project is null) return;
        var l = new PaintLayer { Name = $"Layer {Layers.Count + 1}" };
        Layers.Add(l);
        Project.Layers.Add(l);
        ActiveLayerIndex = Layers.Count - 1;
        _main.SaveProject();
    }

    [RelayCommand]
    void DeleteLayer()
    {
        if (Project is null || Layers.Count <= 1) return;
        var idx = ActiveLayerIndex;
        Layers.RemoveAt(idx);
        Project.Layers.RemoveAt(idx);
        ActiveLayerIndex = Math.Clamp(idx, 0, Layers.Count - 1);
        _main.SaveProject();
    }

    [RelayCommand]
    void DuplicateLayer()
    {
        var l = CurrentLayer;
        if (l is null || Project is null) return;
        var copy = l.Clone();
        Layers.Insert(ActiveLayerIndex + 1, copy);
        Project.Layers.Insert(ActiveLayerIndex + 1, copy);
        ActiveLayerIndex++;
        _main.SaveProject();
    }

    [RelayCommand]
    void MergeDown()
    {
        if (Project is null) return;

        if (Layers.Count < 2)
        {
            _main.Status = "Nothing to merge — only one layer.";
            return;
        }
        if (ActiveLayerIndex <= 0 || ActiveLayerIndex >= Layers.Count)
        {
            _main.Status = "Select a layer that has a layer below it, then merge.";
            return;
        }

        int topIdx = ActiveLayerIndex;
        int botIdx = topIdx - 1;

        var top = Layers[topIdx];
        var bot = Layers[botIdx];

        var topPx = top.PixelData;
        var botPx = bot.PixelData;
        float topOp = top.Opacity;

        // Unpremultiplied source-over compositing (top over bottom)
        for (int i = 0; i < PaintLayer.Size * PaintLayer.Size; i++)
        {
            int b = i * 4;

            double aTop = topPx[b + 3] / 255.0 * topOp;
            if (aTop <= 0) continue;

            double aBot = botPx[b + 3] / 255.0;
            double aOut = aTop + aBot * (1 - aTop);
            if (aOut <= 0) continue;

            for (int c = 0; c < 3; c++)
            {
                double cTop = topPx[b + c];
                double cBot = botPx[b + c];
                double cOut = (cTop * aTop + cBot * aBot * (1 - aTop)) / aOut;
                botPx[b + c] = (byte)Math.Clamp(Math.Round(cOut), 0, 255);
            }
            botPx[b + 3] = (byte)Math.Clamp(Math.Round(aOut * 255), 0, 255);
        }

        bot.SetPixels(botPx);
        bot.Opacity = 1;

        // Move selection to the survivor before removing, then drop the top layer.
        ActiveLayerIndex = botIdx;
        Layers.RemoveAt(topIdx);
        if (topIdx < Project.Layers.Count) Project.Layers.RemoveAt(topIdx);

        _main.SaveProject();
        _main.Status = $"Merged layer {topIdx + 1} into layer {botIdx + 1}.";
    }

    [RelayCommand]
    void MoveLayerUp()
    {
        int i = ActiveLayerIndex;
        if (i <= 0 || i >= Layers.Count) return;
        (Layers[i - 1], Layers[i]) = (Layers[i], Layers[i - 1]);
        (Project!.Layers[i - 1], Project.Layers[i]) = (Project.Layers[i], Project.Layers[i - 1]);
        ActiveLayerIndex = i - 1;
        _main.SaveProject();
    }

    [RelayCommand]
    void MoveLayerDown()
    {
        int i = ActiveLayerIndex;
        if (i < 0 || i >= Layers.Count - 1) return;
        (Layers[i + 1], Layers[i]) = (Layers[i], Layers[i + 1]);
        (Project!.Layers[i + 1], Project.Layers[i]) = (Project.Layers[i], Project.Layers[i + 1]);
        ActiveLayerIndex = i + 1;
        _main.SaveProject();
    }

    [RelayCommand]
    void ClearLayer()
    {
        var l = CurrentLayer;
        if (l is null) return;
        HandleStrokeBegin();
        l.Clear();
        _main.SaveProject();
    }

    // ── Transform active layer ─────────────────────────────
    [RelayCommand]
    void FlipHorizontal() => TransformActive((x, y) => (15 - x, y));

    [RelayCommand]
    void FlipVertical()   => TransformActive((x, y) => (x, 15 - y));

    [RelayCommand]
    void RotateCw()       => TransformActive((x, y) => (15 - y, x));

    [RelayCommand]
    void RotateCcw()      => TransformActive((x, y) => (y, 15 - x));

    void TransformActive(Func<int, int, (int, int)> map)
    {
        var l = CurrentLayer;
        if (l is null) return;
        HandleStrokeBegin();
        var src = l.PixelData;
        var dst = new byte[PaintLayer.ByteCount];
        for (int y = 0; y < 16; y++)
            for (int x = 0; x < 16; x++)
            {
                var (nx, ny) = map(x, y);
                if (nx < 0 || nx >= 16 || ny < 0 || ny >= 16) continue;
                int si = (y * 16 + x) * 4;
                int di = (ny * 16 + nx) * 4;
                dst[di + 0] = src[si + 0];
                dst[di + 1] = src[si + 1];
                dst[di + 2] = src[si + 2];
                dst[di + 3] = src[si + 3];
            }
        l.SetPixels(dst);
        _main.SaveProject();
    }

    // ── Palette ────────────────────────────────────────────
    [RelayCommand]
    void PickColor(Color c)
    {
        CurrentColor = c;
        HexInput = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        PushRecent(c);
    }

    void PushRecent(Color c)
    {
        for (int i = RecentColors.Count - 1; i >= 0; i--)
            if (RecentColors[i] == c) RecentColors.RemoveAt(i);

        RecentColors.Insert(0, c);
        while (RecentColors.Count > RecentLimit)
            RecentColors.RemoveAt(RecentColors.Count - 1);
    }

    [RelayCommand]
    void ApplyHexInput()
    {
        var c = ParseHexOrDefault(HexInput, Colors.White);
        CurrentColor = c;
    }

    [RelayCommand]
    void AddCustomColor()
    {
        if (Project is null) return;
        var c = ParseHexOrDefault(HexInput, Colors.White);
        var hex = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        if (!CustomPalette.Any(x => $"#{x.R:X2}{x.G:X2}{x.B:X2}" == hex))
        {
            CustomPalette.Add(c);
            Project.CustomPalette.Add(hex);
            _main.SaveProject();
        }
    }

    [RelayCommand]
    void RemoveCustomColor(Color c)
    {
        if (Project is null) return;
        var hex = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        Project.CustomPalette.Remove(hex);
        var found = CustomPalette.FirstOrDefault(x =>
            $"#{x.R:X2}{x.G:X2}{x.B:X2}" == hex);
        if (found != default) CustomPalette.Remove(found);
        _main.SaveProject();
    }

    static Color ParseHexOrDefault(string s, Color fallback)
    {
        try { return Color.Parse(s); }
        catch { return fallback; }
    }

    // ── Zoom ───────────────────────────────────────────────
    [RelayCommand] void ZoomIn()  => Zoom = Math.Min(64, Zoom + 4);
    [RelayCommand] void ZoomOut() => Zoom = Math.Max(8,  Zoom - 4);

    // ── Save composited texture as PNG ─────────────────────
    [RelayCommand]
    async System.Threading.Tasks.Task SavePng()
    {
        var owner = _main.GetWindowForDialog();
        if (owner is null) return;

        var file = await owner.StorageProvider.SaveFilePickerAsync(
            new FilePickerSaveOptions
            {
                Title = "Save texture as PNG",
                SuggestedFileName = (Texture?.Name ?? "texture") + ".png",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("PNG image") { Patterns = new[] { "*.png" } },
                },
            });
        if (file is null) return;

        try
        {
            var composite = ComposeForExport();
            if (composite is null) return;

            await using var stream = await file.OpenWriteAsync();
            composite.Save(stream);
            _main.Status = "Saved: " + file.Name;
        }
        catch (Exception ex) { _main.Status = "Save failed: " + ex.Message; }
    }

    Bitmap? ComposeForExport()
    {
        if (TextureKey is null || BasePixels is null) return null;

        var data = new byte[PaintLayer.ByteCount];
        Array.Copy(BasePixels, data, data.Length);

        // Composite layers in order (bottom to top)
        foreach (var layer in Layers)
        {
            if (!layer.Visible) continue;
            var px = layer.PixelData;
            float op = layer.Opacity;
            for (int i = 0; i < 16 * 16; i++)
            {
                int b = i * 4;
                double a = px[b + 3] / 255.0 * op;
                if (a <= 0) continue;
                for (int c = 0; c < 3; c++)
                    data[b + c] = (byte)Math.Clamp(
                        px[b + c] * a + data[b + c] * (1 - a), 0, 255);
                data[b + 3] = (byte)Math.Clamp(
                    255 * (a + data[b + 3] / 255.0 * (1 - a)), 0, 255);
            }
        }

        // Convert to Bitmap via TexImage
        var img = new TexImage(16, 16);
        for (int i = 0; i < 16 * 16; i++)
        {
            int s = i * 4;
            img.Data[s + 0] = data[s + 0];
            img.Data[s + 1] = data[s + 1];
            img.Data[s + 2] = data[s + 2];
            img.Data[s + 3] = data[s + 3];
        }
        return img.ToBitmap();
    }
}