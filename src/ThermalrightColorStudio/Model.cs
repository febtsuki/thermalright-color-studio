using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ThermalrightColorStudio;

public enum Pattern { Linear, Radial, Mirror, Conic, Stripes, Zones, Solid }
public enum Motion { Still, Flow, PingPong, Cycle, Breathe, Sweep, Ripple, Temperature, Load }
public record ColorStop(string Hex, double Position);
public sealed class StudioSettings
{
    public int SchemaVersion { get; set; } = 1;
    public string Name { get; set; } = "自定义配色";
    public List<ColorStop> Palette { get; set; } = [new("#FFB7D5", 0), new("#C9B8FF", .5), new("#A9DFFF", 1)];
    public Pattern Pattern { get; set; }
    public Motion Motion { get; set; }
    public double Angle { get; set; } = 0;
    public double Brightness { get; set; } = 50;
    public double Period { get; set; } = 6;
    public double CenterX { get; set; } = .5;
    public double CenterY { get; set; } = .5;
    public double Repeats { get; set; } = 2;
    public bool HardEdges { get; set; }
    public bool Reverse { get; set; }
    public bool ReverseMotion { get; set; }
    public bool Enabled { get; set; } = true;
    public bool Simulated { get; set; } = true;
    public string Source { get; set; } = "CPU";
    public bool Rotate { get; set; }
    public double RotateSeconds { get; set; } = 2;
    public bool Fahrenheit { get; set; }
    public bool TrayOnClose { get; set; } = true;
    public bool AutoStartOutput { get; set; } = true;
    public bool LightTheme { get; set; }
    public bool SignalRgbSync { get; set; }
    public bool SignalRgbFallback { get; set; } = true;
    public double SignalRgbSmoothing { get; set; }
    public bool SignalRgbFiltersEnabled { get; set; } = true;
    public double SignalRgbExposure { get; set; }
    public double SignalRgbBrightness { get; set; } = 100;
    public double SignalRgbSaturation { get; set; } = 100;
    public double SignalRgbContrast { get; set; } = 100;
    public bool ReadMotherboard { get; set; }
    public double LinkLow { get; set; } = 30;
    public double LinkHigh { get; set; } = 90;
    public Dictionary<string, string> ZoneColors { get; set; } = new() { ["Temperature"] = "#FFB7D5", ["Power"] = "#FFB36B", ["Clock"] = "#C9B8FF", ["Load"] = "#A9DFFF", ["Labels"] = "#B8FFE1" };
    public Dictionary<string, string> SensorMappings { get; set; } = new();
    public void Validate()
    {
        if (SchemaVersion != 1) throw new InvalidDataException("不支持此配置版本。");
        if (Palette is null || Palette.Count is < 1 or > 16 || Palette.Any(s => s is null || !Rgb.TryParse(s.Hex, out _) || !double.IsFinite(s.Position) || s.Position is < 0 or > 1)) throw new InvalidDataException("色板需要 1–16 个有效 HEX 色标，位置为 0–1。");
        if (!Enum.IsDefined(Pattern) || !Enum.IsDefined(Motion)) throw new InvalidDataException("未知效果类型。");
        foreach (var x in new[] { Angle, Brightness, Period, CenterX, CenterY, Repeats, RotateSeconds, LinkLow, LinkHigh, SignalRgbSmoothing, SignalRgbExposure, SignalRgbBrightness, SignalRgbSaturation, SignalRgbContrast }) if (!double.IsFinite(x)) throw new InvalidDataException("配置数值无效。");
        SignalRgbSmoothing = Math.Clamp(SignalRgbSmoothing, 0, 100);
        SignalRgbExposure = Math.Clamp(SignalRgbExposure, -3, 3);
        SignalRgbBrightness = Math.Clamp(SignalRgbBrightness, 0, 200);
        SignalRgbSaturation = Math.Clamp(SignalRgbSaturation, 0, 200);
        SignalRgbContrast = Math.Clamp(SignalRgbContrast, 0, 200);
        Brightness = Math.Clamp(Brightness, 0, 100); Period = Math.Clamp(Period, .5, 60);
        Angle = ((Angle % 360) + 360) % 360; CenterX = Math.Clamp(CenterX, 0, 1); CenterY = Math.Clamp(CenterY, 0, 1);
        Repeats = Math.Clamp(Repeats, 1, 12); RotateSeconds = Math.Clamp(RotateSeconds, 1, 60);
        if (LinkHigh <= LinkLow) throw new InvalidDataException("联动上限必须大于下限。");
        if (Source is not ("CPU" or "GPU")) throw new InvalidDataException("无效数据源。");
        if (ZoneColors is null || new[] { "Temperature", "Power", "Clock", "Load", "Labels" }.Any(z => !ZoneColors.ContainsKey(z)) || ZoneColors.Any(x => !Rgb.TryParse(x.Value, out _))) throw new InvalidDataException("区域颜色无效。");
        if (SensorMappings is null) throw new InvalidDataException("传感器配置无效。");
    }
}
public readonly record struct Rgb(byte R, byte G, byte B)
{
    public static bool TryParse(string? text, out Rgb color)
    {
        color = default;
        if (text is null || text.Length != 7 || text[0] != '#' || !int.TryParse(text.AsSpan(1), System.Globalization.NumberStyles.HexNumber, null, out int n)) return false;
        color = new((byte)(n >> 16), (byte)(n >> 8), (byte)n); return true;
    }
    public static Rgb Parse(string text) => TryParse(text, out var c) ? c : throw new FormatException("请输入 #RRGGBB 格式颜色。");
    public string Hex => $"#{R:X2}{G:X2}{B:X2}";
    public Rgb Scale(double f) => new((byte)Math.Clamp(Math.Round(R * f), 0, 255), (byte)Math.Clamp(Math.Round(G * f), 0, 255), (byte)Math.Clamp(Math.Round(B * f), 0, 255));
    public static Rgb Lerp(Rgb a, Rgb b, double t) => new((byte)Math.Round(a.R + (b.R - a.R) * t), (byte)Math.Round(a.G + (b.G - a.G) * t), (byte)Math.Round(a.B + (b.B - a.B) * t));
}
public static class SettingsStore
{
    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    public static string Root { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ThermalrightColorStudio");
    public static string Serialize(StudioSettings s) => JsonSerializer.Serialize(s, Json);
    public static StudioSettings Deserialize(string text) { var s = JsonSerializer.Deserialize<StudioSettings>(text, Json) ?? throw new InvalidDataException("空配置。"); s.Validate(); return s; }
    public static void Save(string file, StudioSettings s)
    {
        s.Validate(); Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
        File.WriteAllText(file + ".tmp", Serialize(s)); File.Move(file + ".tmp", file, true);
    }
    public static StudioSettings Load(string file) => Deserialize(File.ReadAllText(file));
}
public static class EffectEngine
{
    public static double Wrap(double t) => t - Math.Floor(t);
    public static Rgb Sample(IReadOnlyList<ColorStop> palette, double t, bool hard, bool cyclic)
    {
        var stops = palette.OrderBy(p => p.Position).ToArray();
        if (stops.Length == 1) return Rgb.Parse(stops[0].Hex);
        t = cyclic ? Wrap(t) : Math.Clamp(t, 0, 1);
        // In looping effects the last half-open interval returns to the first color.
        // Normalize stop positions into [0, (n-1)/n] to reserve a closing interval.
        double scale = cyclic ? (stops.Length - 1.0) / stops.Length : 1;
        var a = stops[0]; double ap = a.Position * scale;
        if (t < ap) return Rgb.Parse(a.Hex);
        for (int i = 1; i < stops.Length; i++)
        {
            var b = stops[i]; double bp = b.Position * scale;
            if (t < bp) return hard ? Rgb.Parse(a.Hex) : Rgb.Lerp(Rgb.Parse(a.Hex), Rgb.Parse(b.Hex), bp == ap ? 1 : (t - ap) / (bp - ap));
            a = b; ap = bp;
        }
        return cyclic && !hard ? Rgb.Lerp(Rgb.Parse(a.Hex), Rgb.Parse(stops[0].Hex), Math.Clamp((t - ap) / Math.Max(1e-9, 1 - ap), 0, 1)) : Rgb.Parse(a.Hex);
    }
    public static Rgb At(StudioSettings s, double x, double y, string zone, double time, double? temp = null, double? load = null)
    {
        if (!s.Enabled) return default;
        double rad = s.Angle * Math.PI / 180, dx = (x - s.CenterX) * 404, dy = (y - s.CenterY) * 260;
        double linear = .5 + ((x - .5) * 404 * Math.Cos(rad) + (y - .5) * 260 * Math.Sin(rad)) / (404 * Math.Abs(Math.Cos(rad)) + 260 * Math.Abs(Math.Sin(rad)));
        double t = s.Pattern switch { Pattern.Radial => Math.Sqrt(dx * dx + dy * dy) / 240.2, Pattern.Mirror => Math.Abs(2 * linear - 1), Pattern.Conic => Wrap(Math.Atan2(dy, dx) / (2 * Math.PI) - s.Angle / 360), Pattern.Stripes => Wrap(linear * s.Repeats), Pattern.Solid => 0, _ => linear };
        if (s.Reverse) t = 1 - t;
        double phase = time / s.Period * (s.ReverseMotion ? -1 : 1), factor = 1;
        bool cyclic = s.Motion is Motion.Flow or Motion.Cycle || s.Pattern is Pattern.Conic or Pattern.Stripes;
        switch (s.Motion)
        {
            case Motion.Flow: t += phase; break;
            case Motion.PingPong: t += .5 * Math.Sin(phase * Math.Tau); t = 1 - Math.Abs(Wrap(t / 2) * 2 - 1); break;
            case Motion.Cycle: t = phase; break;
            case Motion.Breathe: factor = .1 + .9 * (.5 - .5 * Math.Cos(phase * Math.Tau)); break;
            case Motion.Sweep: factor = .08 + .92 * Math.Pow(.5 + .5 * Math.Cos((linear - phase) * Math.Tau), 12); break;
            case Motion.Ripple: factor = .08 + .92 * Math.Pow(.5 + .5 * Math.Cos((Math.Sqrt(dx * dx + dy * dy) * s.Repeats - phase) * Math.Tau), 3); break;
            case Motion.Temperature: if (!temp.HasValue) return default; t = (temp.Value - s.LinkLow) / (s.LinkHigh - s.LinkLow); cyclic = false; break;
            case Motion.Load: if (!load.HasValue) return default; t = (load.Value - s.LinkLow) / (s.LinkHigh - s.LinkLow); cyclic = false; break;
        }
        var color = s.Pattern == Pattern.Zones && s.Motion is not (Motion.Temperature or Motion.Load or Motion.Cycle) ? Rgb.Parse(s.ZoneColors[zone]) : Sample(s.Palette, t, s.HardEdges || s.Pattern == Pattern.Stripes, cyclic);
        return color.Scale(s.Brightness / 100 * factor);
    }
}
