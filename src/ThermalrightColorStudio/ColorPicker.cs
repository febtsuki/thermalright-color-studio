using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ThermalrightColorStudio;
public static class ColorMath
{
    public static Rgb FromHsv(double hue, double saturation, double value)
    {
        hue = ((hue % 360) + 360) % 360; saturation = Math.Clamp(saturation, 0, 1); value = Math.Clamp(value, 0, 1);
        double c = value * saturation, x = c * (1 - Math.Abs(hue / 60 % 2 - 1)), m = value - c;
        (double r, double g, double b) = hue switch { < 60 => (c, x, 0d), < 120 => (x, c, 0d), < 180 => (0d, c, x), < 240 => (0d, x, c), < 300 => (x, 0d, c), _ => (c, 0d, x) };
        return new((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }
    public static (double Hue, double Saturation, double Value) ToHsv(Rgb color)
    {
        double r = color.R / 255d, g = color.G / 255d, b = color.B / 255d, max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), delta = max - min;
        double hue = delta == 0 ? 0 : max == r ? 60 * ((g - b) / delta % 6) : max == g ? 60 * ((b - r) / delta + 2) : 60 * ((r - g) / delta + 4);
        return ((hue + 360) % 360, max == 0 ? 0 : delta / max, max);
    }
}
public sealed class HueSquare : FrameworkElement
{
    const double Center = 130, Outer = 122, Inner = 103, SquareStart = 60, SquareSize = 140;
    double hue, saturation, value; int dragging; static BitmapSource? ring;
    public event Action<Rgb>? ColorChanged;
    public Rgb Color => ColorMath.FromHsv(hue, saturation, value);
    public HueSquare(Rgb initial) { Width = Height = 260; Focusable = true; Cursor = Cursors.Cross; SetColor(initial); ToolTip = "外环选择色相；方形内横向为饱和度、纵向为明度。"; }
    public void SetColor(Rgb color)
    {
        var hsv = ColorMath.ToHsv(color); if (hsv.Saturation > 0) hue = hsv.Hue;
        saturation = hsv.Saturation; value = hsv.Value; InvalidateVisual();
    }
    static SolidColorBrush Brush(Rgb c) => new(System.Windows.Media.Color.FromRgb(c.R, c.G, c.B));
    static BitmapSource Ring()
    {
        if (ring is not null) return ring;
        const int size = 260; var pixels = new byte[size * size * 4];
        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
        {
            double dx = x + .5 - Center, dy = y + .5 - Center, radius = Math.Sqrt(dx * dx + dy * dy);
            double opacity = Math.Clamp(Math.Min(Outer - radius, radius - Inner), 0, 1);
            if (opacity <= 0) continue;
            var c = ColorMath.FromHsv(Math.Atan2(dy, dx) * 180 / Math.PI, 1, 1); int p = (y * size + x) * 4;
            pixels[p] = (byte)(c.B * opacity); pixels[p + 1] = (byte)(c.G * opacity); pixels[p + 2] = (byte)(c.R * opacity); pixels[p + 3] = (byte)(255 * opacity);
        }
        ring = BitmapSource.Create(size, size, 96, 96, PixelFormats.Pbgra32, null, pixels, size * 4); ring.Freeze(); return ring;
    }
    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, 260, 260)); dc.DrawImage(Ring(), new Rect(0, 0, 260, 260));
        var square = new Rect(SquareStart, SquareStart, SquareSize, SquareSize);
        dc.DrawRectangle(new LinearGradientBrush(Colors.White, Brush(ColorMath.FromHsv(hue, 1, 1)).Color, 0), null, square);
        dc.DrawRectangle(new LinearGradientBrush(Colors.Transparent, Colors.Black, 90), new Pen(StudioTheme.Border, 1), square);
        double angle = hue * Math.PI / 180, radius = (Outer + Inner) / 2;
        Mark(dc, new(Center + radius * Math.Cos(angle), Center + radius * Math.Sin(angle)), 5);
        Mark(dc, new(SquareStart + saturation * SquareSize, SquareStart + (1 - value) * SquareSize), 5);
    }
    static void Mark(DrawingContext dc, Point point, double radius)
    { dc.DrawEllipse(null, new Pen(Brushes.White, 3), point, radius, radius); dc.DrawEllipse(null, new Pen(Brushes.Black, 1), point, radius, radius); }
    internal bool SelectAt(Point point, bool begin)
    {
        double dx = point.X - Center, dy = point.Y - Center, radius = Math.Sqrt(dx * dx + dy * dy);
        if (begin) dragging = new Rect(SquareStart, SquareStart, SquareSize, SquareSize).Contains(point) ? 1 : radius >= Inner - 4 && radius <= Outer + 4 ? 2 : 0;
        if (dragging == 1) { saturation = Math.Clamp((point.X - SquareStart) / SquareSize, 0, 1); value = 1 - Math.Clamp((point.Y - SquareStart) / SquareSize, 0, 1); }
        else if (dragging == 2) hue = (Math.Atan2(dy, dx) * 180 / Math.PI + 360) % 360;
        else return false;
        InvalidateVisual(); ColorChanged?.Invoke(Color); return true;
    }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e) { Focus(); if (SelectAt(e.GetPosition(this), true)) { CaptureMouse(); e.Handled = true; } }
    protected override void OnMouseMove(MouseEventArgs e) { if (IsMouseCaptured && e.LeftButton == MouseButtonState.Pressed) SelectAt(e.GetPosition(this), false); }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e) { dragging = 0; ReleaseMouseCapture(); }
    protected override void OnLostMouseCapture(MouseEventArgs e) { dragging = 0; }
}
public sealed class ColorPickerWindow : Window
{
    readonly HueSquare picker; readonly TextBox hex, red, green, blue; readonly Border current; readonly TextBlock error;
    public string SelectedHex => picker.Color.Hex;
    public ColorPickerWindow(string initial)
    {
        Title = "选择颜色"; Width = 388; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; Background = StudioTheme.Surface; Foreground = StudioTheme.Text;
        FontFamily = new FontFamily("Microsoft YaHei UI"); FontSize = 13; ShowInTaskbar = false;
        var root = new StackPanel { Margin = new Thickness(24) }; Content = root;
        root.Children.Add(new TextBlock { Text = "色相 · 饱和度 · 明度", Foreground = StudioTheme.Muted });
        picker = new(Rgb.Parse(initial)) { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 12) }; root.Children.Add(picker);
        var swatches = new Grid { Height = 34, Margin = new Thickness(0, 0, 0, 14) }; swatches.ColumnDefinitions.Add(new()); swatches.ColumnDefinitions.Add(new());
        var old = new Border { Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(initial)), ToolTip = "原颜色" }; swatches.Children.Add(old);
        current = new Border { ToolTip = "新颜色" }; Grid.SetColumn(current, 1); swatches.Children.Add(current); root.Children.Add(swatches);
        TextBox Input(int width) => new() { Width = width, Background = StudioTheme.Input, Foreground = StudioTheme.Text, BorderBrush = StudioTheme.Border, Padding = new Thickness(7), Margin = new Thickness(0, 4, 8, 8) };
        hex = Input(120); red = Input(83); green = Input(83); blue = Input(83);
        root.Children.Add(new TextBlock { Text = "HEX", Foreground = StudioTheme.Muted }); root.Children.Add(hex);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var field in new[] { ("R", red), ("G", green), ("B", blue) }) { var block = new StackPanel(); block.Children.Add(new TextBlock { Text = field.Item1, Foreground = StudioTheme.Muted }); block.Children.Add(field.Item2); row.Children.Add(block); } root.Children.Add(row);
        error = new TextBlock { Foreground = StudioTheme.Accent, TextWrapping = TextWrapping.Wrap }; root.Children.Add(error);
        void ApplyHex() { if (!Rgb.TryParse(hex.Text.Trim().ToUpperInvariant(), out var c)) { error.Text = "HEX 格式：#RRGGBB"; return; } picker.SetColor(c); Sync(); }
        void ApplyRgb() { if (!byte.TryParse(red.Text, out var r) || !byte.TryParse(green.Text, out var g) || !byte.TryParse(blue.Text, out var b)) { error.Text = "RGB 范围：0–255"; return; } picker.SetColor(new(r, g, b)); Sync(); }
        hex.LostKeyboardFocus += (_, _) => ApplyHex(); hex.KeyDown += (_, e) => { if (e.Key == Key.Enter) { ApplyHex(); e.Handled = true; } };
        foreach (var field in new[] { red, green, blue }) { field.LostKeyboardFocus += (_, _) => ApplyRgb(); field.KeyDown += (_, e) => { if (e.Key == Key.Enter) { ApplyRgb(); e.Handled = true; } }; }
        Button Button(string text, Action action) { var b = new Button { Content = text, Padding = new Thickness(16, 8, 16, 8), Margin = new Thickness(0, 8, 8, 0), Background = StudioTheme.Button, Foreground = StudioTheme.Text, BorderBrush = StudioTheme.Border }; b.Click += (_, _) => action(); return b; }
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = Button("取消", () => DialogResult = false); cancel.IsCancel = true; buttons.Children.Add(cancel);
        buttons.Children.Add(Button("应用颜色", () => { if (error.Text.Length == 0) DialogResult = true; })); root.Children.Add(buttons);
        picker.ColorChanged += _ => Sync(); Sync();
    }
    void Sync() { var c = picker.Color; hex.Text = c.Hex; red.Text = c.R.ToString(); green.Text = c.G.ToString(); blue.Text = c.B.ToString(); current.Background = new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B)); error.Text = ""; }
}
