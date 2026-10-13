using System.IO;

namespace ThermalrightColorStudio;
public static class SelfTests
{
    public static string Run()
    {
        var passed = new List<string>();
        void Check(string name, bool ok) { if (!ok) throw new Exception("FAIL: " + name); passed.Add("PASS " + name); }
        var s = new StudioSettings { Brightness = 100 };
        Check("布局 93 段及线序为完整排列", PanelLayout.Rects.Length == 93 && PanelLayout.WireMap.Order().SequenceEqual(Enumerable.Range(0, 93)));
        Check("静态渐变与时间无关", EffectEngine.At(s, .3, .7, "Power", 0) == EffectEngine.At(s, .3, .7, "Power", 100));
        Check("渐变端点", EffectEngine.At(s, 0, .5, "Power", 0).Hex == s.Palette[0].Hex && EffectEngine.At(s, 1, .5, "Power", 0).Hex == s.Palette[^1].Hex);
        s.Angle = 90; Check("90 度纵向渐变", EffectEngine.At(s, .1, .4, "Power", 0) == EffectEngine.At(s, .9, .4, "Power", 0));
        s.Angle = 45; Check("45 度斜向渐变", EffectEngine.At(s, .2, .2, "Power", 0) != EffectEngine.At(s, .8, .8, "Power", 0));
        s.Motion = Motion.Flow; var a = EffectEngine.At(s, .3, .4, "Power", 0); Check("流动循环闭合", a == EffectEngine.At(s, .3, .4, "Power", s.Period));
        var before = EffectEngine.Sample(s.Palette, .99999, false, true); var after = EffectEngine.Sample(s.Palette, .00001, false, true);
        Check("循环接缝连续", Math.Abs(before.R - after.R) + Math.Abs(before.G - after.G) + Math.Abs(before.B - after.B) <= 3);
        s.Motion = Motion.Still; s.Pattern = Pattern.Zones; Check("独立区域颜色", EffectEngine.At(s, .5, .5, "Power", 0).Hex == s.ZoneColors["Power"]);
        s.Pattern = Pattern.Linear; s.Motion = Motion.Temperature; Check("传感器失效不伪造联动值", EffectEngine.At(s, .5, .5, "Power", 0) == default);
        s.Enabled = false; Check("关闭输出为黑色", PanelLayout.Frame(s, new(45, 90, 4500, 50), "CPU", 0).All(c => c == default)); s.Enabled = true;
        var mask = PanelLayout.Mask(new(null, null, null, null), "CPU", false); Check("缺失指标不显示 0", mask.Count(b => b) == 1);
        mask = PanelLayout.Mask(new(1000, 1000, 10000, 101), "CPU", false); Check("溢出指标留空", mask.Count(b => b) == 1);
        mask = PanelLayout.Mask(new(45, 90, 4500, 100), "GPU", false); Check("100% 与 GPU 标签正确", mask[91] && mask[92] && mask[1] && !mask[0]);
        var reports = HidService.Encode(Enumerable.Repeat(new Rgb(255, 100, 0), 93).ToArray());
        Check("HID 分包数量及 Report ID", reports.Length == 5 && reports.All(r => r.Length == 65 && r[0] == 0));
        var raw = reports.SelectMany(r => r.Skip(1)).ToArray();
        Check("HID 长度/通道缩放/末包填充", raw[12] == 2 && raw[16] == 23 && raw[17] == 1 && raw[20] == 102 && raw[21] == 40 && raw.Skip(299).All(b => b == 0));
        var roundtrip = SettingsStore.Deserialize(SettingsStore.Serialize(s)); Check("配置序列化往返", SettingsStore.Serialize(s) == SettingsStore.Serialize(roundtrip));
        bool rejected = false; try { SettingsStore.Deserialize("{\"SchemaVersion\":999}"); } catch (InvalidDataException) { rejected = true; } Check("拒绝未知配置版本", rejected);
        foreach (Pattern p in Enum.GetValues<Pattern>()) foreach (Motion m in Enum.GetValues<Motion>()) { s.Pattern = p; s.Motion = m; foreach (double t in new[] { 0, .5, 6.0 }) foreach (var zone in s.ZoneColors.Keys) _ = EffectEngine.At(s, .2, .9, zone, t, 45, 60); }
        Check("全部空间/动画组合可渲染", true);
        Check("HSV 基色与端点", ColorMath.FromHsv(0, 1, 1).Hex == "#FF0000" && ColorMath.FromHsv(120, 1, 1).Hex == "#00FF00" && ColorMath.FromHsv(240, 1, 1).Hex == "#0000FF" && ColorMath.FromHsv(300, 0, 1).Hex == "#FFFFFF" && ColorMath.FromHsv(25, 1, 0) == default);
        foreach (var c in new[] { new Rgb(255, 102, 77), new Rgb(1, 2, 3), new Rgb(186, 120, 255), new Rgb(255, 255, 255), default(Rgb) })
        { var hsv = ColorMath.ToHsv(c); Check("HSV/RGB 精确往返 " + c.Hex, ColorMath.FromHsv(hsv.Hue, hsv.Saturation, hsv.Value) == c); }
        var migrated = SettingsStore.Deserialize("{\"SchemaVersion\":1,\"TrayOnClose\":false}");
        Check("旧配置迁移保留关闭选项并启用自动输出", !migrated.TrayOnClose && migrated.AutoStartOutput && !migrated.LightTheme);
        var token = Enumerable.Range(0, 16).Select(i => (byte)i).ToArray();
        var packet = new byte[SignalBridge.PacketLength]; packet[0] = 84; packet[1] = 67; packet[2] = 83; packet[3] = 82; packet[4] = 1; token.CopyTo(packet, 5); packet[21] = 42;
        for (int i = 0; i < 93; i++) { packet[25 + i * 3] = (byte)i; packet[26 + i * 3] = 255; }
        Check("SignalRGB 93 段帧解码及序号", SignalBridge.Decode(packet, token, out var colors, out var sequence) && colors.Length == 93 && colors[92] == new Rgb(92, 255, 0) && sequence == 42);
        Check("SignalRGB 拒绝截断帧", !SignalBridge.Decode(packet[..^1], token, out _, out _));
        var other = (byte[])token.Clone(); other[0]++;
        Check("SignalRGB 拒绝错误会话", !SignalBridge.Decode(packet, other, out _, out _));
        packet[4] = 2; Check("SignalRGB 拒绝未知协议", !SignalBridge.Decode(packet, token, out _, out _)); packet[4] = 1;
        s.Enabled = true; s.Brightness = 50; var masked = SignalBridge.MaskFrame(colors, s, new(null, null, null, null), "CPU");
        Check("SignalRGB 保留真实数字掩码与亮度", masked[0] == colors[0].Scale(.5) && masked.Skip(1).All(c => c == default));
        s.Enabled = false; Check("SignalRGB 关闭输出", SignalBridge.MaskFrame(colors, s, new(45, 90, 4500, 50), "CPU").All(c => c == default));
        var flat = Enumerable.Repeat(new Rgb(23, 107, 241), 93).ToArray();
        Check("高斯平滑保持均匀颜色", SignalColorSmoothing.Apply(flat, 100).SequenceEqual(flat));
        Check("平滑为零精确保留原色", SignalColorSmoothing.Apply(colors, 0).SequenceEqual(colors));
        var original = colors.ToArray(); _ = SignalColorSmoothing.Apply(colors, 50);
        Check("平滑不修改接收原始帧", colors.SequenceEqual(original));
        var impulse = new Rgb[93]; impulse[7] = new Rgb(255, 0, 0);
        var softened = SignalColorSmoothing.Apply(impulse, 40);
        Check("平滑柔化空间突变并保持远处颜色", softened[7].R is > 0 and < 255 && softened[8].R > 0 && softened[83].R == 0);
        var step = PanelLayout.Rects.Select(r => r.X + r.Width / 2 < 200 ? new Rgb(255, 0, 0) : new Rgb(0, 0, 255)).ToArray();
        Check("平滑使色块边界出现中间色", SignalColorSmoothing.Apply(step, 70).Any(c => c.R > 0 && c.B > 0));
        s.Enabled = true; s.Brightness = 50; s.SignalRgbSmoothing = 60;
        var smoothMask = SignalBridge.MaskFrame(step, s, new(45, 90, 4500, 50), "CPU");
        var expectedMask = PanelLayout.Mask(new(45, 90, 4500, 50), "CPU", s.Fahrenheit);
        var expectedColors = SignalColorSmoothing.Apply(step, 60);
        Check("平滑先于数字掩码且保留亮度", Enumerable.Range(0,93).All(i => smoothMask[i] == (expectedMask[i] ? expectedColors[i].Scale(.5) : default)));
        s.Enabled = false;
        Check("启用平滑时关闭输出仍全黑", SignalBridge.MaskFrame(step, s, new(45, 90, 4500, 50), "CPU").All(c => c == default));
        Check("旧配置不自动改变同步色彩", migrated.SignalRgbSmoothing == 0);
        var smoothSettings = SettingsStore.Deserialize("{\"SignalRgbSmoothing\":150}");
        Check("平滑参数限定范围且可保存", smoothSettings.SignalRgbSmoothing == 100 && SettingsStore.Deserialize(SettingsStore.Serialize(smoothSettings)).SignalRgbSmoothing == 100);
        var grading = new StudioSettings();
        Check("同步调色默认精确保留原始帧", SignalColorFilters.Apply(colors, grading).SequenceEqual(colors));
        grading.SignalRgbExposure = 2; grading.SignalRgbFiltersEnabled = false;
        Check("调色旁路保留参数但不改变颜色", SignalColorFilters.Apply(colors, grading).SequenceEqual(colors) && grading.SignalRgbExposure == 2);
        grading = new StudioSettings { SignalRgbExposure = 1 };
        var exposed = SignalColorFilters.Apply([new Rgb(128,128,128)], grading)[0];
        Check("正一档曝光提升线性光亮度", exposed.R is >= 175 and <= 176 && exposed.R == exposed.G && exposed.G == exposed.B);
        grading = new StudioSettings { SignalRgbBrightness = 200 };
        Check("亮度增益提升颜色", SignalColorFilters.Apply([new Rgb(64,32,16)], grading)[0] == new Rgb(128,64,32));
        grading = new StudioSettings { SignalRgbSaturation = 0 };
        var gray = SignalColorFilters.Apply([new Rgb(255,0,0)], grading)[0];
        Check("零饱和度转为等通道灰色", gray == new Rgb(54,54,54));
        grading.SignalRgbSaturation = 200;
        var saturated = SignalColorFilters.Apply([new Rgb(100,120,140)], grading)[0];
        Check("提高饱和度扩大颜色差异", saturated.B - saturated.R > 40);
        grading = new StudioSettings { SignalRgbContrast = 200 };
        var contrasted = SignalColorFilters.Apply([new Rgb(64,128,192)], grading)[0];
        Check("提高对比度拉开明暗", contrasted.R <= 1 && contrasted.G is >= 128 and <= 129 && contrasted.B == 255);
        grading = new StudioSettings { SignalRgbExposure = 3, SignalRgbContrast = 0, SignalRgbBrightness = 200 };
        Check("调色保持纯黑区域熄灭", SignalColorFilters.Apply([default], grading)[0] == default);
        grading.SignalRgbBrightness = 0;
        Check("零亮度增益熄灭所有颜色", SignalColorFilters.Apply(colors, grading).All(c => c == default));
        grading = new StudioSettings { SignalRgbExposure = 3, SignalRgbBrightness = 200 };
        Check("调色过曝截断且不回绕", SignalColorFilters.Apply([new Rgb(200,200,200)], grading)[0] == new Rgb(255,255,255));
        var filterInput = colors.ToArray(); _ = SignalColorFilters.Apply(colors, grading);
        Check("同步调色不修改接收原始帧", colors.SequenceEqual(filterInput));
        grading.SignalRgbSmoothing = 40; grading.Brightness = 50;
        var gradedFrame = SignalBridge.MaskFrame(step, grading, new(45,90,4500,50), "CPU");
        var gradeExpected = SignalColorFilters.Apply(SignalColorSmoothing.Apply(step, 40), grading);
        Check("同步调色在平滑后且保留掩码和整体亮度", Enumerable.Range(0,93).All(i => gradedFrame[i] == (expectedMask[i] ? gradeExpected[i].Scale(.5) : default)));
        Check("旧配置迁移保持中性调色", migrated.SignalRgbFiltersEnabled && migrated.SignalRgbExposure == 0 && migrated.SignalRgbBrightness == 100 && migrated.SignalRgbSaturation == 100 && migrated.SignalRgbContrast == 100);
        var gradeSaved = SettingsStore.Deserialize(SettingsStore.Serialize(grading));
        Check("调色参数配置往返", gradeSaved.SignalRgbExposure == 3 && gradeSaved.SignalRgbBrightness == 200 && gradeSaved.SignalRgbSmoothing == 40);
        var gradeLimits = SettingsStore.Deserialize("{\"SignalRgbExposure\":99,\"SignalRgbBrightness\":-1,\"SignalRgbSaturation\":300,\"SignalRgbContrast\":-5}");
        Check("调色参数限定安全范围", gradeLimits.SignalRgbExposure == 3 && gradeLimits.SignalRgbBrightness == 0 && gradeLimits.SignalRgbSaturation == 200 && gradeLimits.SignalRgbContrast == 0);
        bool badGrade = false; try { new StudioSettings { SignalRgbExposure = double.NaN }.Validate(); } catch (InvalidDataException) { badGrade = true; }
        Check("拒绝无效调色数值", badGrade);
        return string.Join(Environment.NewLine, passed) + $"\n{passed.Count} checks passed.\n";
    }
}
