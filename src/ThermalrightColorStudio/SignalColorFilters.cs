namespace ThermalrightColorStudio;

public static class SignalColorFilters
{
    public static Rgb[] Apply(Rgb[] colors, StudioSettings settings)
    {
        if (!settings.SignalRgbFiltersEnabled || IsNeutral(settings)) return colors;
        var output = new Rgb[colors.Length];
        double exposure = System.Math.Pow(2, settings.SignalRgbExposure);
        double saturation = settings.SignalRgbSaturation / 100;
        double contrast = settings.SignalRgbContrast / 100;
        double brightness = settings.SignalRgbBrightness / 100;
        for (int i = 0; i < colors.Length; i++)
        {
            var color = colors[i];
            // Preserve intentional black/off regions even with reduced contrast.
            if (color == default) continue;
            double r = ToSrgb(ToLinear(color.R / 255.0) * exposure);
            double g = ToSrgb(ToLinear(color.G / 255.0) * exposure);
            double b = ToSrgb(ToLinear(color.B / 255.0) * exposure);
            double luminance = .2126 * r + .7152 * g + .0722 * b;
            byte Channel(double value) => ToByte(((luminance + (value - luminance) * saturation - .5) * contrast + .5) * brightness);
            // Clip only after all adjustments; never overwrite the received frame.
            output[i] = new(Channel(r), Channel(g), Channel(b));
        }
        return output;
    }

    static bool IsNeutral(StudioSettings s) => s.SignalRgbExposure == 0 && s.SignalRgbSaturation == 100 && s.SignalRgbContrast == 100 && s.SignalRgbBrightness == 100;
    static double ToLinear(double value) => value <= .04045 ? value / 12.92 : System.Math.Pow((value + .055) / 1.055, 2.4);
    static double ToSrgb(double value) => value <= .0031308 ? value * 12.92 : 1.055 * System.Math.Pow(value, 1 / 2.4) - .055;
    static byte ToByte(double value) => (byte)System.Math.Clamp(System.Math.Round(value * 255), 0, 255);
}
