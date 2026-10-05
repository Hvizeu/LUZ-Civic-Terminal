namespace LuzDesktop;
public sealed class CutBorder : Decorator
{
    public static readonly StyledProperty<IBrush?> BackgroundProperty = Border.BackgroundProperty.AddOwner<CutBorder>();
    public static readonly StyledProperty<IBrush?> BorderBrushProperty = Border.BorderBrushProperty.AddOwner<CutBorder>();
    public static readonly StyledProperty<Thickness> BorderThicknessProperty = Border.BorderThicknessProperty.AddOwner<CutBorder>();
    public IBrush? Background { get => GetValue(BackgroundProperty); set => SetValue(BackgroundProperty, value); }
    public IBrush? BorderBrush { get => GetValue(BorderBrushProperty); set => SetValue(BorderBrushProperty, value); }
    public Thickness BorderThickness { get => GetValue(BorderThicknessProperty); set => SetValue(BorderThicknessProperty, value); }
    public static readonly StyledProperty<double> CutProperty = AvaloniaProperty.Register<CutBorder, double>(nameof(Cut), 10);
    public double Cut { get => GetValue(CutProperty); set => SetValue(CutProperty, value); }
    static CutBorder() => AffectsRender<CutBorder>(CutProperty, BackgroundProperty, BorderBrushProperty, BorderThicknessProperty);
    public override void Render(DrawingContext context)
    {
        double width = Bounds.Width, height = Bounds.Height, inset = BorderThickness.Left / 2;
        double cut = Math.Min(Cut, Math.Min(width, height) / 2);
        var shape = new StreamGeometry(); using (var path = shape.Open())
        {
            path.BeginFigure(new Point(cut, inset), true);
            foreach (var point in new[] { new Point(width-inset,inset),new Point(width-inset,height-cut),new Point(width-cut,height-inset),new Point(inset,height-inset),new Point(inset,cut) }) path.LineTo(point);
            path.EndFigure(true);
        }
        context.DrawGeometry(Background, BorderBrush != null ? new Pen(BorderBrush, BorderThickness.Left) : null, shape);
    }
}
