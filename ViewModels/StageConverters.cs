using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using McTextureTweaker.Models;

namespace McTextureTweaker.ViewModels;

public static class StageConverters
{
    public static readonly IValueConverter IsOne   = new Cmp(1);
    public static readonly IValueConverter IsTwo   = new Cmp(2);
    public static readonly IValueConverter IsThree = new Cmp(3);
    public static readonly IValueConverter IsFour  = new Cmp(4);

    sealed class Cmp : IValueConverter
    {
        readonly int _n;
        public Cmp(int n) => _n = n;
        public object Convert(object? v, Type t, object? p, CultureInfo c) => v is int i && i == _n;
        public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
    }
}

public static class ModelKindConverters
{
    public static readonly IValueConverter IsCube  = new Eq(ModelKind.Cube);
    public static readonly IValueConverter IsCross = new Eq(ModelKind.Cross);

    sealed class Eq : IValueConverter
    {
        readonly ModelKind _k;
        public Eq(ModelKind k) => _k = k;
        public object Convert(object? v, Type t, object? p, CultureInfo c) => v is ModelKind mk && mk == _k;
        public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
    }
}

/// <summary>Color → SolidColorBrush (for palette swatches).</summary>
public sealed class ColorToBrush : IValueConverter
{
    public static readonly ColorToBrush Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is Color c) return new SolidColorBrush(c);
        if (value is IBrush b) return b;
        return new SolidColorBrush(Colors.Transparent);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}