using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace McTextureTweaker.Models;

public enum PaintTool
{
    Pencil,
    Eraser,
    Fill,
    Eyedropper,
    Line,
    Rect,
    RectFill,
    Ellipse,
    EllipseFill,
}

public sealed class PaintProject
{
    public List<PaintLayer> Layers { get; set; } = new();
    public int ActiveLayerIndex { get; set; }
    public List<string> CustomPalette { get; set; } = new();
    public bool BaseIsRecolored { get; set; } = true;
}

public sealed partial class PaintLayer : ObservableObject
{
    public const int Size = 16;
    public const int ByteCount = Size * Size * 4;

    [ObservableProperty] private string _name = "Layer";
    [ObservableProperty] private bool _visible = true;
    [ObservableProperty] private float _opacity = 1.0f;

    [ObservableProperty] private string _pixelsB64 = "";

    [JsonIgnore]
    public byte[] PixelData
    {
        get
        {
            if (string.IsNullOrEmpty(PixelsB64)) return new byte[ByteCount];
            try
            {
                var b = Convert.FromBase64String(PixelsB64);
                if (b.Length == ByteCount) return b;
                var resized = new byte[ByteCount];
                Array.Copy(b, resized, Math.Min(b.Length, ByteCount));
                return resized;
            }
            catch { return new byte[ByteCount]; }
        }
    }

    public void SetPixels(byte[] data)
    {
        if (data.Length != ByteCount) throw new ArgumentException("Expected " + ByteCount + " bytes.");
        PixelsB64 = Convert.ToBase64String(data);
    }

    public void Clear() => PixelsB64 = "";
    public bool IsEmpty => string.IsNullOrEmpty(PixelsB64);

    public PaintLayer Clone() => new()
    {
        Name = Name + " copy",
        Visible = Visible,
        Opacity = Opacity,
        PixelsB64 = PixelsB64,
    };
}