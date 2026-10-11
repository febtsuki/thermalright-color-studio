namespace ThermalrightColorStudio;

// Gaussian weighting of the 93 received spatial samples, before applying digit masks.
// This cannot reconstruct the full SignalRGB canvas or subdivide a physical LED segment.
public static class SignalColorSmoothing
{
    sealed record Kernel(double Strength, double[,] Weights);
    static readonly object gate = new();
    static Kernel? cached;

    public static Rgb[] Apply(Rgb[] colors, double strength)
    {
        if (colors.Length != PanelLayout.Rects.Length) throw new System.ArgumentException("平滑需要 93 段颜色。");
        if (!double.IsFinite(strength)) throw new System.ArgumentException("平滑强度无效。");
        strength = System.Math.Clamp(strength, 0, 100);
        if (strength == 0) return colors;
        Kernel kernel;
        lock (gate)
        {
            if (cached is null || cached.Strength != strength) cached = Build(strength);
            kernel = cached;
        }
        var output = new Rgb[colors.Length];
        for (int i = 0; i < colors.Length; i++)
        {
            double r = 0, g = 0, b = 0;
            for (int j = 0; j < colors.Length; j++)
            {
                double w = kernel.Weights[i, j];
                r += colors[j].R * w; g += colors[j].G * w; b += colors[j].B * w;
            }
            output[i] = new(ToByte(r), ToByte(g), ToByte(b));
        }
        return output;
    }

    static byte ToByte(double value) => (byte)System.Math.Clamp(System.Math.Round(value), 0, 255);
    static Kernel Build(double strength)
    {
        var rects = PanelLayout.Rects;
        var weights = new double[rects.Length, rects.Length];
        // Physical aspect ratio is retained. At 100%, sigma is ~20% of panel width.
        double sigma = strength * .8, cutoff = 9 * sigma * sigma;
        for (int i = 0; i < rects.Length; i++)
        {
            double total = 0;
            for (int j = 0; j < rects.Length; j++)
            {
                double dx = rects[i].X + rects[i].Width / 2 - rects[j].X - rects[j].Width / 2;
                double dy = rects[i].Y + rects[i].Height / 2 - rects[j].Y - rects[j].Height / 2;
                double distance = dx * dx + dy * dy;
                double w = distance <= cutoff ? System.Math.Exp(-distance / (2 * sigma * sigma)) : 0;
                weights[i, j] = w; total += w;
            }
            for (int j = 0; j < rects.Length; j++) weights[i, j] /= total;
        }
        return new(strength, weights);
    }
}
