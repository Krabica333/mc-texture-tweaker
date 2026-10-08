using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Markup.Xaml;

namespace McTextureTweaker.Views.Controls;

public partial class ViewModeToggle : UserControl
{
    public static readonly StyledProperty<bool> IsTextureModeProperty =
        AvaloniaProperty.Register<ViewModeToggle, bool>(nameof(IsTextureMode), false,
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<string> LabelProperty =
        AvaloniaProperty.Register<ViewModeToggle, string>(nameof(Label), "By Block");

    public bool IsTextureMode
    {
        get => GetValue(IsTextureModeProperty);
        set => SetValue(IsTextureModeProperty, value);
    }

    /// <summary>Derived label — swaps automatically when IsTextureMode changes.</summary>
    public string Label
    {
        get => GetValue(LabelProperty);
        private set => SetValue(LabelProperty, value);
    }

    public ViewModeToggle()
    {
        AvaloniaXamlLoader.Load(this);
        UpdateLabel();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsTextureModeProperty)
            UpdateLabel();
    }

    void UpdateLabel()
        => SetValue(LabelProperty, IsTextureMode ? "By Texture" : "By Block");
}