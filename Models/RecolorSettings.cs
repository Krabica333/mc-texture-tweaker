using CommunityToolkit.Mvvm.ComponentModel;

namespace TextureTinter.Models;

public enum RecolorMethod { Average, Classic, Perceptual, Pick, Hsv }
public enum ModelKind { Cube, Cross }

public partial class RecolorSettings : ObservableObject
{
    [ObservableProperty] private RecolorMethod _method = RecolorMethod.Average;
    [ObservableProperty] private RecolorMethod? _method2;
    [ObservableProperty] private int _blendPct = 50;

    [ObservableProperty] private float _tint = 1.0f;
    [ObservableProperty] private float _sat = 1.0f;
    [ObservableProperty] private float _flattenHue;
    [ObservableProperty] private float _flattenSat;
    [ObservableProperty] private float _flattenVal;
    [ObservableProperty] private float _hueSplit = 6f;
    [ObservableProperty] private float _shadingGain = 1.0f;
    [ObservableProperty] private float _shadowSat = 25f;
    [ObservableProperty] private float _extremeComp = 40f;
    [ObservableProperty] private float _brightShadowDark = 30f;
    [ObservableProperty] private float _tolerance = 100f;
    public int[]? ReferencePixel { get; set; }

    [ObservableProperty] private float _tint2 = 1.0f;
    [ObservableProperty] private float _sat2 = 1.0f;
    [ObservableProperty] private float _flattenHue2;
    [ObservableProperty] private float _flattenSat2;
    [ObservableProperty] private float _flattenVal2;
    [ObservableProperty] private float _hueSplit2 = 6f;
    [ObservableProperty] private float _shadingGain2 = 1.0f;
    [ObservableProperty] private float _shadowSat2 = 25f;
    [ObservableProperty] private float _extremeComp2 = 40f;
    [ObservableProperty] private float _brightShadowDark2 = 30f;
    [ObservableProperty] private float _tolerance2 = 100f;

    [ObservableProperty] private Adjustments _pre = new();
    [ObservableProperty] private Adjustments _post = new();

    public RecolorSettings()
    {
        Pre.PropertyChanged += (_, e) => OnPropertyChanged("Pre." + e.PropertyName);
        Post.PropertyChanged += (_, e) => OnPropertyChanged("Post." + e.PropertyName);
    }
}

public partial class Adjustments : ObservableObject
{
    [ObservableProperty] private float _brightness;
    [ObservableProperty] private float _contrast = 1f;
    [ObservableProperty] private float _vibrance;
    [ObservableProperty] private float _clarity;
    [ObservableProperty] private float _hueShift;

    public bool IsIdentity =>
        Brightness == 0 && Contrast == 1f && Vibrance == 0 && Clarity == 0 && HueShift == 0;

    public Adjustments Clone() => new()
    {
        Brightness = Brightness,
        Contrast = Contrast,
        Vibrance = Vibrance,
        Clarity = Clarity,
        HueShift = HueShift,
    };
}

public sealed class CompensationSettings
{
    public float DarkStart { get; set; } = 100f;
    public float DarkBoost { get; set; } = 2.5f;
    public float BrightStart { get; set; } = 170f;
    public float BrightBoost { get; set; } = 1.0f;
    public float Curve { get; set; } = 1.0f;
    public float Fit { get; set; } = 0.12f;
}

public sealed class TextureEntry
{
    public string? Name { get; set; }
    public string? Category { get; set; }
    public string? TexId { get; set; }
    public System.Collections.Generic.List<string> Blocks { get; set; } = new();
    public RecolorSettings Settings { get; set; } = new();
    public System.Collections.Generic.List<ExceptionProfile> Exceptions { get; set; } = new();
    public ModelKind ModelKind { get; set; } = ModelKind.Cube;
}

public sealed class ExceptionProfile : RecolorSettings
{
    public System.Collections.Generic.List<string> Dyes { get; set; } = new();
}