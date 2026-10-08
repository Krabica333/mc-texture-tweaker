using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace McTextureTweaker.Views;

public partial class MainWindow : Window
{
    public MainWindow() => AvaloniaXamlLoader.Load(this);
}