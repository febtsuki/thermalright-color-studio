using System.Windows;
using System.Windows.Media;
using System.Globalization;

namespace ThermalrightColorStudio;
public sealed class PanelView : FrameworkElement
{
    public Rgb[] Colors { get; set; } = new Rgb[93];
    public bool[] Mask { get; set; } = new bool[93];
    public int SelectedSegment { get; set; } = -1;
    public Action<int>? SegmentClicked;
    public PanelView()
    {
        MouseLeftButtonDown += (_, e) =>
        {
            var pt = e.GetPosition(this); double scale = Math.Min(ActualWidth / 460, ActualHeight / 330);
            double x = (pt.X - (ActualWidth - 460 * scale) / 2) / scale - 18 + 26;
            double y = (pt.Y - (ActualHeight - 330 * scale) / 2) / scale - 28 + 98;
            for (int i = 0; i < 93; i++) { var r = PanelLayout.Rects[i]; if (x >= r.X - 3 && x <= r.X + r.Width + 3 && y >= r.Y - 3 && y <= r.Y + r.Height + 3) { SelectedSegment = i; SegmentClicked?.Invoke(i); InvalidateVisual(); break; } }
        };
    }
    static Brush Brush(Rgb c) => new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B));
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc); double scale = Math.Min(ActualWidth / 460, ActualHeight / 330);
        dc.PushTransform(new TranslateTransform((ActualWidth - 460 * scale) / 2, (ActualHeight - 330 * scale) / 2)); dc.PushTransform(new ScaleTransform(scale, scale));
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(5, 9, 15)), new Pen(new SolidColorBrush(Color.FromRgb(38, 48, 68)), 1), new Rect(0, 0, 460, 330), 18, 18);
        string[] labels = ["CPU", "GPU", "°C", "°F", "W", "MHz", "%"];
        for (int i = 0; i < 93; i++)
        {
            var r = PanelLayout.Rects[i]; var rect = new Rect(r.X - 26 + 18, r.Y - 98 + 28, r.Width, r.Height);
            var brush = Mask[i] ? Brush(Colors[i]) : new SolidColorBrush(Color.FromRgb(20, 27, 35));
            if (i < 7)
            {
                var text = new FormattedText(labels[i], CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), i < 2 ? 17 : 20, brush, 1);
                dc.DrawText(text, new Point(rect.X, rect.Y));
            }
            else
            {
                double cut = Math.Min(rect.Width, rect.Height) / 2;
                var geometry = new StreamGeometry(); using (var ctx = geometry.Open())
                {
                    ctx.BeginFigure(new Point(rect.X + cut, rect.Y), true, true);
                    ctx.PolyLineTo([new(rect.Right - cut, rect.Y), new(rect.Right, rect.Y + cut), new(rect.Right, rect.Bottom - cut), new(rect.Right - cut, rect.Bottom), new(rect.X + cut, rect.Bottom), new(rect.X, rect.Bottom - cut), new(rect.X, rect.Y + cut)], true, false);
                }
                dc.DrawGeometry(brush, null, geometry);
            }
            if (i == SelectedSegment) dc.DrawRoundedRectangle(null, new Pen(Brushes.White, 1), new Rect(rect.X - 3, rect.Y - 3, rect.Width + 6, rect.Height + 6), 3, 3);
        }
        dc.Pop(); dc.Pop();
    }
}
