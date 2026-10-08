using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using TextureTinter.Models;
using TextureTinter.ViewModels;

namespace TextureTinter.Views;

public partial class Stage2View : UserControl
{
    static readonly RecolorMethod[] MethodOrder =
    {
        RecolorMethod.Average,
        RecolorMethod.Classic,
        RecolorMethod.Perceptual,
        RecolorMethod.Pick,
        RecolorMethod.Hsv,
    };

    // Which sliders each method actually uses (key = slider field name without the "2" suffix).
    static readonly Dictionary<RecolorMethod, HashSet<string>> Uses = BuildUses();

    static Dictionary<RecolorMethod, HashSet<string>> BuildUses()
    {
        var d = new Dictionary<RecolorMethod, HashSet<string>>();
        d[RecolorMethod.Classic] = new HashSet<string> { "Tint", "Sat" };
        d[RecolorMethod.Average] = new HashSet<string> { "Tint", "Sat" };
        d[RecolorMethod.Perceptual] = new HashSet<string> { "Tint", "Sat", "HueSplit", "ShadingGain" };
        d[RecolorMethod.Pick] = new HashSet<string> { "Tint", "Sat", "Tolerance" };
        d[RecolorMethod.Hsv] = new HashSet<string>
        {
            "FlattenHue", "FlattenSat", "FlattenVal",
            "ShadingGain", "HueSplit", "ShadowSat",
            "ExtremeComp", "BrightShadowDark",
        };
        return d;
    }

    MainViewModel? _vm;
    bool _loaded;
    bool _suppress;

    public Stage2View()
    {
        AvaloniaXamlLoader.Load(this);
        DataContextChanged += (_, _) => Attach();
        Loaded += (_, _) => Attach();
    }

    void Attach()
    {
        if (_loaded || DataContext is not MainViewModel vm) return;
        _vm = vm;
        _loaded = true;

        _vm.PropertyChanged += OnVmPropertyChanged;
        LoadMethodCombos();
        UpdateSliderVisibility();
    }

    void OnMethodChanged(object? s, SelectionChangedEventArgs e)
    {
        if (_suppress || _vm?.CurrentProfile is not { } p) return;
        if (s is not ComboBox cb) return;
        if (cb.SelectedIndex < 0 || cb.SelectedIndex >= MethodOrder.Length) return;
        p.Method = MethodOrder[cb.SelectedIndex];
        UpdateSliderVisibility();
    }

    void OnMethod2Changed(object? s, SelectionChangedEventArgs e)
    {
        if (_suppress || _vm?.CurrentProfile is not { } p) return;
        if (s is not ComboBox cb) return;
        p.Method2 = cb.SelectedIndex <= 0 ? null : MethodOrder[cb.SelectedIndex - 1];
        UpdateSliderVisibility();
    }

    void OnVmPropertyChanged(object? s, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedTexture) ||
            e.PropertyName == nameof(MainViewModel.CurrentProfile) ||
            e.PropertyName == nameof(MainViewModel.ExceptionTabIndex))
        {
            LoadMethodCombos();
            UpdateSliderVisibility();
        }
        else if (e.PropertyName == nameof(MainViewModel.Method2Visible))
        {
            UpdateSliderVisibility();
        }
    }

    void LoadMethodCombos()
    {
        if (_vm?.CurrentProfile is not { } p) return;
        _suppress = true;
        try
        {
            var m1 = this.FindControl<ComboBox>("MethodCombo");
            if (m1 is not null)
            {
                int idx = Array.IndexOf(MethodOrder, p.Method);
                if (m1.SelectedIndex != idx) m1.SelectedIndex = idx;
            }
            var m2 = this.FindControl<ComboBox>("Method2Combo");
            if (m2 is not null)
            {
                int idx = p.Method2 is null ? 0 : Array.IndexOf(MethodOrder, p.Method2.Value) + 1;
                if (m2.SelectedIndex != idx) m2.SelectedIndex = idx;
            }
        }
        finally { _suppress = false; }
    }

    void UpdateSliderVisibility()
{
    var p = _vm?.CurrentProfile;
    if (p is null) return;

    var used1 = Uses.TryGetValue(p.Method, out var u1) ? u1 : new HashSet<string>();
    var used2 = p.Method2.HasValue && Uses.TryGetValue(p.Method2.Value, out var u2)
        ? u2
        : new HashSet<string>();

    foreach (var panel in this.GetVisualDescendants().OfType<Control>())
    {
        if (panel.Name is null) continue;
        if (panel.Name.StartsWith("M1_"))
            panel.IsVisible = used1.Contains(panel.Name.Substring(3));
        else if (panel.Name.StartsWith("M2_"))
            panel.IsVisible = used2.Contains(panel.Name.Substring(3));
    }
}
}