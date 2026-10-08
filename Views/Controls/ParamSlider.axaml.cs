using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace McTextureTweaker.Views.Controls;

public partial class ParamSlider : UserControl
{
    public static readonly StyledProperty<string> LabelProperty =
        AvaloniaProperty.Register<ParamSlider, string>(nameof(Label), "");

    public static readonly StyledProperty<double> MinimumProperty =
        AvaloniaProperty.Register<ParamSlider, double>(nameof(Minimum), 0);

    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<ParamSlider, double>(nameof(Maximum), 1);

    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<ParamSlider, double>(nameof(Value), 0,
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<double> DefaultValueProperty =
        AvaloniaProperty.Register<ParamSlider, double>(nameof(DefaultValue), 0);

    public static readonly StyledProperty<double> LabelWidthProperty =
        AvaloniaProperty.Register<ParamSlider, double>(nameof(LabelWidth), 190);

    public string Label { get => GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public double Minimum { get => GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double DefaultValue { get => GetValue(DefaultValueProperty); set => SetValue(DefaultValueProperty, value); }
    public double LabelWidth { get => GetValue(LabelWidthProperty); set => SetValue(LabelWidthProperty, value); }

    public ParamSlider() => AvaloniaXamlLoader.Load(this);

    void OnResetClicked(object? sender, RoutedEventArgs e) => Value = DefaultValue;
}