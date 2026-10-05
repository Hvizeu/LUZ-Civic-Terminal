using Avalonia.Data;
using Avalonia.Controls.Documents;
using Avalonia.Markup.Xaml;
using Avalonia.Themes.Fluent;

namespace LuzDesktop;
public static class TerminalTheme
{
    public static IBrush Ink = Brush("#111D24"), Panel = Brush("#263238"), Edge = Brush("#526064"), Text = Brush("#ECE4D4"), Muted = Brush("#B3BCBD"), Gold = Brush("#D5B176"), Teal = Brush("#60C9CB");
    public static SolidColorBrush Brush(string hex) => new(Color.Parse(hex));
    public static Control Section(string title, Control content)
    {
        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        grid.Children.Add(new CutBorder { Cut = 14, BorderBrush = Edge, BorderThickness = new Thickness(1.5), Background = Panel,
            Child = new TextBlock { Text = title.ToUpperInvariant(), FontWeight = FontWeight.SemiBold, FontSize = 15, Foreground = Gold, Margin = new Thickness(12, 8, 12, 7) } });
        var plate = new Border { Padding = new Thickness(0, 10, 0, 0), Child = content }; Grid.SetRow(plate, 1); grid.Children.Add(plate); return grid;
    }
    public static void Install(Application app)
    {
        app.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
        var fluent = new FluentTheme();
        fluent.Palettes[Avalonia.Styling.ThemeVariant.Dark] = new ColorPaletteResources { Accent = Color.Parse("#60C9CB") };
        app.Styles.Add(fluent); app.Styles.Add(new TerminalStyles());
    }
}
public sealed partial class TerminalStyles : Avalonia.Styling.Styles
{
    public TerminalStyles() => AvaloniaXamlLoader.Load(this);
}
