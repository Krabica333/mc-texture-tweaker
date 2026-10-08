using System;
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

public sealed class TextureItem
{
    public string Key { get; }
    public string Name { get; }
    public Bitmap? Thumbnail { get; }
    public TextureEntry Entry { get; }
    public TextureItem(string key, string name, Bitmap? thumb, TextureEntry entry)
    {
        Key = key; Name = name; Thumbnail = thumb; Entry = entry;
    }
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

public sealed partial class PreviewTile : ObservableObject
{
    public string DyeName { get; }
    public Bitmap Image { get; }
    public bool InException { get; }

    public PreviewTile(string dyeName, Bitmap image, bool inException)
    {
        DyeName = dyeName;
        Image = image;
        InException = inException;
    }
}

public sealed partial class DyeCheck : ObservableObject
{
    public string Name { get; }
    [ObservableProperty] private bool _isChecked;
    public event EventHandler? Changed;

    public DyeCheck(string name, bool isChecked) { Name = name; _isChecked = isChecked; }
    partial void OnIsCheckedChanged(bool value) => Changed?.Invoke(this, EventArgs.Empty);
}