using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace McTextureTweaker.Views;

public sealed class InputDialog : Window
{
    public InputDialog(string title, string prompt, string? initial = null)
    {
        Title = title;
        Width = 420;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var input = new TextBox { Text = initial ?? "", Margin = new Thickness(0, 0, 0, 12) };
        var ok = new Button { Content = "OK", Padding = new Thickness(20, 6), IsDefault = true };
        var cancel = new Button { Content = "Cancel", Padding = new Thickness(20, 6), IsCancel = true };

        ok.Click += (_, _) => Close(input.Text);
        cancel.Click += (_, _) => Close(null);

        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = prompt },
                input,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { cancel, ok },
                },
            },
        };

        Opened += (_, _) => input.Focus();
    }
}