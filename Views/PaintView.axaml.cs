using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using McTextureTweaker.Rendering;
using McTextureTweaker.ViewModels;

namespace McTextureTweaker.Views;

public partial class PaintView : UserControl
{
    public PaintView()
    {
        AvaloniaXamlLoader.Load(this);
        DataContextChanged += (_, _) => Attach();
        Loaded += (_, _) => Attach();
    }

    bool _hooked;

    void Attach()
    {
        if (_hooked || DataContext is not MainViewModel vm) return;
        var canvas = this.FindControl<PaintCanvas>("Canvas");
        if (canvas is null) return;

        canvas.StrokeBegin += (_, _) => vm.Paint.HandleStrokeBegin();
        canvas.ColorPicked += (_, c) => vm.Paint.HandleColorPicked(c);
        _hooked = true;
    }
}