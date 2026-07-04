using System.Windows;
using System.Windows.Media;
using MediaBrush = System.Windows.Media.Brush;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaPen = System.Windows.Media.Pen;
using WpfPoint = System.Windows.Point;

namespace Winotch;

public sealed class SparklineControl : FrameworkElement
{
    private const int SampleCapacity = 60;
    private const double FillOpacity = 0.08;
    private const double LineThickness = 1;

    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values),
        typeof(IReadOnlyList<double>),
        typeof(SparklineControl),
        new FrameworkPropertyMetadata(Array.Empty<double>(), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke),
        typeof(MediaBrush),
        typeof(SparklineControl),
        new FrameworkPropertyMetadata(MediaBrushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<double> Values
    {
        get => (IReadOnlyList<double>)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public MediaBrush Stroke
    {
        get => (MediaBrush)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var points = SparklinePointMapper.Map(Values, SampleCapacity, ActualWidth, ActualHeight);
        if (points.Count == 0)
        {
            return;
        }

        if (points.Count > 1)
        {
            drawingContext.DrawGeometry(FillBrush(), null, BuildFillGeometry(points));
        }

        var pen = new MediaPen(Stroke, LineThickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };
        drawingContext.DrawGeometry(null, pen, BuildLineGeometry(points));
    }

    private MediaBrush FillBrush()
    {
        var brush = Stroke.CloneCurrentValue();
        brush.Opacity = FillOpacity;
        return brush;
    }

    private Geometry BuildLineGeometry(IReadOnlyList<SparklinePoint> points)
    {
        var geometry = new StreamGeometry();
        using var context = geometry.Open();
        var first = points[0];
        context.BeginFigure(new WpfPoint(first.X, first.Y), isFilled: false, isClosed: false);
        if (points.Count == 1)
        {
            context.LineTo(new WpfPoint(Math.Min(ActualWidth - 1, first.X + 2), first.Y), isStroked: true, isSmoothJoin: false);
        }
        else
        {
            for (var index = 1; index < points.Count; index++)
            {
                context.LineTo(new WpfPoint(points[index].X, points[index].Y), isStroked: true, isSmoothJoin: true);
            }
        }

        geometry.Freeze();
        return geometry;
    }

    private Geometry BuildFillGeometry(IReadOnlyList<SparklinePoint> points)
    {
        var baseline = Math.Max(0, ActualHeight - 1);
        var geometry = new StreamGeometry();
        using var context = geometry.Open();
        context.BeginFigure(new WpfPoint(points[0].X, baseline), isFilled: true, isClosed: true);
        foreach (var point in points)
        {
            context.LineTo(new WpfPoint(point.X, point.Y), isStroked: false, isSmoothJoin: true);
        }

        context.LineTo(new WpfPoint(points[^1].X, baseline), isStroked: false, isSmoothJoin: false);
        geometry.Freeze();
        return geometry;
    }
}
