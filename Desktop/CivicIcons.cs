using Avalonia.Controls.Documents;
using Avalonia.Data;
using Avalonia.Controls.Shapes;
namespace LuzDesktop;

public enum CivicSymbol { Registry, Catalogue, Maintenance, Import, Apply, Play, Pinned, Dependencies }

public static class CivicIcons
{
    // Shared 24-unit grid, two-unit strokes and 45-degree corners.
    private static readonly Dictionary<CivicSymbol, string> Paths = new()
    {
        [CivicSymbol.Registry] = "M4,4 L19,4 22,7 22,18 20,20 2,20 2,6 Z M14,10 H19 M14,15 H19 M7,8 L9,8 10,9 10,11 9,12 7,12 6,11 6,9 Z M5,17 V16 L7,14 H9 L11,16 V17",
        [CivicSymbol.Catalogue] = "M2,3 H6 L8,5 V21 H2 Z M9,3 H13 L15,5 V21 H9 Z M16,3 H20 L22,5 V21 H16 Z M5,8 V10 M12,8 V10 M19,8 V10 M5,16 V18 M12,16 V18 M19,16 V18",
        [CivicSymbol.Maintenance] = "M14,3 H18 L14,7 V10 H17 L21,6 V10 L17,14 H13 L6,21 H3 V18 L10,11 V7 Z",
        [CivicSymbol.Import] = "M12,2 V14 M7,9 L12,14 17,9 M5,13 H3 V20 L4,21 H20 L21,20 V13 H19",
        [CivicSymbol.Apply] = "M5,2 H15 L20,7 V20 L18,22 H4 L2,20 V5 Z M7,13 L10,16 16,10",
        [CivicSymbol.Play] = "M9,3 L21,12 9,21 Z M2,12 H5",
        [CivicSymbol.Pinned] = "M14,3 L21,10 18,10 14,14 V17 L7,10 H10 L14,6 Z M10,14 L3,21",
        [CivicSymbol.Dependencies] = "M10,2 H14 L15,3 V7 H9 V3 Z M12,7 V12 M5,16 V12 H19 V16 M3,16 H7 L8,17 V22 H2 V17 Z M17,16 H21 L22,17 V22 H16 V17 Z"
    };
    private static readonly Dictionary<CivicSymbol, Geometry> Geometry = Paths.ToDictionary(pair => pair.Key, pair =>
    {
        var geometry = Avalonia.Media.Geometry.Parse(pair.Value); return geometry;
    });

    public static Control Glyph(CivicSymbol symbol, double size = 20)
    {
        var path = new Avalonia.Controls.Shapes.Path { Data = Geometry[symbol], StrokeThickness = 2, StrokeJoin = PenLineJoin.Miter };
        path.Bind(Shape.StrokeProperty, path.GetObservable(TextElement.ForegroundProperty));
        var canvas = new Canvas { Width = 24, Height = 24 }; canvas.Children.Add(path);
        return new Viewbox { Child = canvas, Width = size, Height = size, Stretch = Stretch.Uniform, IsHitTestVisible = false, Focusable = false, VerticalAlignment = VerticalAlignment.Center };
    }

    public static StackPanel Label(CivicSymbol symbol, string text, double size = 20, bool controlContent = false)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        if (controlContent) panel.Bind(TextElement.ForegroundProperty, new Binding("Foreground") { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor) { AncestorType = typeof(ContentPresenter) } });
        var icon = Glyph(symbol, size); icon.Margin = new Thickness(0, 0, 9, 0); panel.Children.Add(icon);
        panel.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        return panel;
    }

#if PREVIEW_TOOL
    public static void Export(string folder)
    {
        System.IO.Directory.CreateDirectory(folder);
        foreach (var (symbol, path) in Paths)
            System.IO.File.WriteAllText(System.IO.Path.Combine(folder, symbol.ToString().ToLowerInvariant() + ".svg"),
                $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linejoin=\"miter\" stroke-miterlimit=\"2\"><path d=\"{path}\"/></svg>");
    }
#endif
}
