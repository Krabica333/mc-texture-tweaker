using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using TextureTinter.Models;

namespace TextureTinter.ViewModels;

public sealed class ProjectEntry
{
    public string Name { get; }
    public DateTime Modified { get; }
    public string Display => $"{Name}    (saved {Modified:yyyy-MM-dd HH:mm})";
    public ProjectEntry(string name, DateTime modified) { Name = name; Modified = modified; }
}

public sealed partial class TextureItem : ObservableObject
{
    public string Key { get; }
    public string Name { get; }
    public Bitmap? Thumbnail { get; }
    public TextureEntry Entry { get; }
    public string GroupKey { get; set; } = "";
    public string GroupName { get; set; } = "";

    [ObservableProperty] private bool _isSelected;

    public TextureItem(string key, string name, Bitmap? thumb, TextureEntry entry)
    {
        Key = key; Name = name; Thumbnail = thumb; Entry = entry;
    }
}

public sealed partial class BlockGroup : ObservableObject
{
    public string Key { get; }
    public string Name { get; }
    public ObservableCollection<TextureItem> Items { get; } = new();

    [ObservableProperty] private bool _isExpanded = false;
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private Bitmap? _representativeThumbnail;

    /// <summary>The most representative texture for this group (for thumbnails and clicking on the group).</summary>
    public TextureItem? MainTexture
    {
        get
        {
            if (Items.Count == 0) return null;
            var normName = Name.ToLowerInvariant().Replace(" ", "_");
            var keyPath = Key.Split(':').LastOrDefault()?.ToLowerInvariant() ?? "";

            var exact = Items.FirstOrDefault(i =>
            {
                var n = (i.Name ?? "").ToLowerInvariant();
                return n == normName || n == keyPath;
            });
            if (exact is not null) return exact;

            var startsWith = Items.FirstOrDefault(i =>
            {
                var n = (i.Name ?? "").ToLowerInvariant();
                return n.StartsWith(normName) || n.StartsWith(keyPath);
            });
            if (startsWith is not null) return startsWith;

            return Items[0];
        }
    }

    public BlockGroup(string key, string name) { Key = key; Name = name; }
}

public sealed class DyeItem
{
    public string Name { get; }
    public string Hex { get; }
    public IBrush Brush { get; }
    public DyeItem(string name, string hex)
    {
        Name = name; Hex = hex;
        try { Brush = new SolidColorBrush(Color.Parse(hex)); }
        catch { Brush = new SolidColorBrush(Colors.Gray); }
    }
}

public sealed class CategoryItem
{
    public string Name { get; }
    public string Hex { get; }
    public IBrush Brush { get; }
    public CategoryItem(string name, string hex)
    {
        Name = name; Hex = hex;
        try { Brush = new SolidColorBrush(Color.Parse(hex)); }
        catch { Brush = new SolidColorBrush(Colors.Gray); }
    }
}

public sealed class PreviewTile
{
    public string DyeName { get; }
    public Bitmap Image { get; }
    public bool InException { get; }
    public PreviewTile(string dyeName, Bitmap image, bool inException)
    { DyeName = dyeName; Image = image; InException = inException; }
}

public sealed partial class DyeCheck : ObservableObject
{
    public string Name { get; }
    [ObservableProperty] private bool _isChecked;
    public event EventHandler? Changed;
    public DyeCheck(string name, bool isChecked) { Name = name; _isChecked = isChecked; }
    partial void OnIsCheckedChanged(bool value) => Changed?.Invoke(this, EventArgs.Empty);
}