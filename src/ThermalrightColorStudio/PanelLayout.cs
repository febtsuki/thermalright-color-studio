// Layout and wire order adapted from thermalright-trcc-linux (GPL-3.0).
// Commit 4d3fdc5b1ee2ca149fea6471a8c020db12067a30. See THIRD-PARTY-NOTICES.md.
namespace ThermalrightColorStudio;
public readonly record struct SegmentRect(double X, double Y, double Width, double Height);
public static class PanelLayout
{
 public static readonly SegmentRect[] Rects = [new(43,98,46,20),new(99,98,46,20),new(211,136,27,27),new(213,179,27,27),new(404,189,22,18),new(249,330,42,26),new(408,335,22,22),new(49,133,34,6),new(81,137,6,32),new(81,173,6,32),new(49,203,34,6),new(45,173,6,32),new(45,137,6,32),new(49,167,34,8),new(105,133,34,6),new(137,137,6,32),new(137,173,6,32),new(105,203,34,6),new(101,173,6,32),new(101,137,6,32),new(105,167,34,8),new(161,133,34,6),new(193,137,6,32),new(193,173,6,32),new(161,203,34,6),new(157,173,6,32),new(157,137,6,32),new(161,167,34,8),new(274,152,27,4),new(300,155,4,24),new(300,182,4,24),new(274,205,27,4),new(271,182,4,24),new(271,155,4,24),new(274,178,27,5),new(317,152,27,4),new(343,155,4,24),new(343,182,4,24),new(317,205,27,4),new(314,182,4,24),new(314,155,4,24),new(317,178,27,5),new(360,152,27,4),new(386,155,4,24),new(386,182,4,24),new(360,205,27,4),new(357,182,4,24),new(357,155,4,24),new(360,178,27,5),new(30,283,34,6),new(62,287,6,32),new(62,323,6,32),new(30,353,34,6),new(26,323,6,32),new(26,287,6,32),new(30,317,34,8),new(86,283,34,6),new(118,287,6,32),new(118,323,6,32),new(86,353,34,6),new(82,323,6,32),new(82,287,6,32),new(86,317,34,8),new(142,283,34,6),new(174,287,6,32),new(174,323,6,32),new(142,353,34,6),new(138,323,6,32),new(138,287,6,32),new(142,317,34,8),new(197,283,34,6),new(229,287,6,32),new(229,323,6,32),new(197,353,34,6),new(193,323,6,32),new(193,287,6,32),new(197,317,34,8),new(321,302,27,4),new(347,305,4,24),new(347,332,4,24),new(321,355,27,4),new(318,332,4,24),new(318,305,4,24),new(321,328,27,5),new(364,302,27,4),new(390,305,4,24),new(390,332,4,24),new(364,355,27,4),new(361,332,4,24),new(361,305,4,24),new(364,328,27,5),new(304,305,4,24),new(304,332,4,24)];
 public static readonly int[] WireMap = [6, 86, 87, 88, 90, 85, 84, 89,
    79, 80, 81, 83, 78, 77, 82,
    91, 5,
    72, 73, 74, 76, 71, 70, 75,
    65, 66, 67, 69, 64, 63, 68,
    58, 59, 60, 62, 57, 56, 61,
    51, 52, 53, 55, 50, 49, 54,
    11, 10, 9, 13, 12, 7, 0, 8,
    18, 17, 16, 20, 19, 14, 1, 15,
    25, 24, 23, 27, 26, 21, 22,
    3, 2,
    32, 31, 30, 34, 33, 28, 29,
    39, 38, 37, 41, 40, 35, 36,
    46, 45, 44, 48, 47, 42, 43,
    4, 92,];
 public static string Zone(int i) => i < 2 ? "Labels" : i is 2 or 3 || i is >= 7 and <= 27 ? "Temperature" : i == 4 || i is >= 28 and <= 48 ? "Power" : i == 5 || i is >= 49 and <= 76 ? "Clock" : "Load";
 static readonly string[] Chars = ["abcdef", "bc", "abdeg", "abcdg", "bcfg", "acdfg", "acdefg", "abc", "abcdefg", "abcdfg"];
 public static bool[] Mask(Metrics metrics, string source, bool fahrenheit, bool all = false)
 {
  var mask = new bool[93];
  if (all) { Array.Fill(mask, true); mask[3] = false; mask[source == "CPU" ? 1 : 0] = false; return mask; }
  mask[source == "CPU" ? 0 : 1] = true;
  void Number(double? value, int start, int digits, int unit, bool partial = false)
  {
   double max = partial ? 100 : Math.Pow(10, digits) - 1;
   if (!value.HasValue || !double.IsFinite(value.Value) || value.Value < 0 || Math.Round(value.Value, MidpointRounding.AwayFromZero) > max) return;
   int n = (int)Math.Round(value.Value, MidpointRounding.AwayFromZero); mask[unit] = true;
   if (partial && n == 100) { mask[91] = mask[92] = true; n = 0; }
   var text = n.ToString().PadLeft(digits, ' ');
   if (partial && mask[91]) text = "00";
   for (int d = 0; d < digits; d++) if (text[d] != ' ') foreach (char c in Chars[text[d] - '0']) mask[start + d * 7 + c - 'a'] = true;
  }
  Number(metrics.Temperature.HasValue ? (fahrenheit ? metrics.Temperature * 1.8 + 32 : metrics.Temperature) : null, 7, 3, fahrenheit ? 3 : 2);
  Number(metrics.Power, 28, 3, 4); Number(metrics.Clock, 49, 4, 5); Number(metrics.Load, 77, 2, 6, true);
  return mask;
 }
 public static Rgb[] Frame(StudioSettings settings, Metrics metrics, string source, double time, bool all = false)
 {
  var mask = Mask(metrics, source, settings.Fahrenheit, all); var frame = new Rgb[93];
  for (int i=0;i<93;i++) if(mask[i]) { var r = Rects[i]; frame[i] = EffectEngine.At(settings, (r.X+r.Width/2-26)/404, (r.Y+r.Height/2-98)/260, Zone(i), time, metrics.Temperature, metrics.Load); }
  return frame;
 }
}
