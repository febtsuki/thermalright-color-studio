using System.IO;
using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Brush = System.Windows.Media.Brush;

namespace ThermalrightColorStudio;
public sealed class MainWindow : Window
{
    static readonly Brush Bg = StudioTheme.Background, Surface = StudioTheme.Surface, Muted = StudioTheme.Muted, Accent = StudioTheme.Accent, Ink = StudioTheme.Text;
    StudioSettings settings = new();
    readonly SensorService sensors = new(); readonly HidService hid = new();
    SignalBridge? signalBridge; TextBlock? signalState; string bridgeError = "";
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    readonly Stopwatch clock = Stopwatch.StartNew();
    readonly StackPanel content = new(), paletteRows = new();
    readonly TextBlock status = Text("正在读取本机传感器…", 12), notice = Text("", 12), previewState = Text("", 12), selected = Text("点击面板上的显示段可查看区域", 12);
    readonly PanelView preview = new() { Height = 340, MinWidth = 370 };
    readonly TextBlock[] metricTexts = Enumerable.Range(0, 4).Select(_ => Text("—", 24)).ToArray();
    readonly List<string> undo = new(), redo = new();
    readonly Dictionary<string, ComboBox> mappingBoxes = new();
    DataGrid? monitor; ComboBox? monitorFilter, deviceSelect, themeSelector, closeSelector; TextBox? diagnosticText;
    Button? streamButton;
    System.Windows.Forms.NotifyIcon? tray;
    bool streaming, paused, rebuilding, closing, connecting; Task? writeTask; long frames; double animationTime, previousTick, lastSample; string page = "colors", mappingSignature = "";
    bool layoutAllowed;
    Rgb[]? previousOutput; double lastOutput; CancellationTokenSource? testStop;
    string settingsFile => Path.Combine(SettingsStore.Root, "settings.json");
    readonly string[] args;
    public MainWindow(string[] args)
    {
        this.args = args;
        Title = "Thermalright Color Studio · 配色工作室"; Width = 1320; Height = 880; MinWidth = 1120; MinHeight = 760;
        Icon = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/StudioIcon.png"));
        Background = Bg; Foreground = Ink; FontFamily = new FontFamily("Microsoft YaHei UI"); FontSize = 13; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        if (args.Contains("--render")) { WindowStartupLocation = WindowStartupLocation.Manual; Left = -20000; Top = 0; ShowInTaskbar = false; }
        try { if (File.Exists(settingsFile)) settings = SettingsStore.Load(settingsFile); } catch (Exception ex) { notice.Text = "配置未加载：" + ex.Message; }
        StudioTheme.Apply(settings.LightTheme); ApplyStyles();
        var root = new Grid(); root.ColumnDefinitions.Add(new() { Width = new GridLength(206) }); root.ColumnDefinitions.Add(new()); Content = root;
        var side = new StackPanel { Margin = new Thickness(20, 26, 16, 20) }; root.Children.Add(new Border { Background = StudioTheme.Sidebar, Child = side });
        side.Children.Add(Text("THERMALRIGHT", 11, Muted)); side.Children.Add(Text("Color Studio", 24)); side.Children.Add(Text("PS120 DIGITAL / v0.3.4", 11, Muted));
        side.Children.Add(new Border { Height = 40 });
        side.Children.Add(Btn("◉   配色工作室", () => ShowPage("colors")));
        side.Children.Add(Btn("▥   硬件监控", () => ShowPage("monitor")));
        side.Children.Add(Btn("⌁   设备与诊断", () => ShowPage("device")));
        side.Children.Add(new Border { Height = 32 });
        side.Children.Add(Text("本地运行 · 无需账号", 11, Muted));
        side.Children.Add(Text("颜色和读数由同一帧发送\n设备控制不改变风扇转速", 11, Muted));
        side.Children.Add(new Border { Height = 25 });
        side.Children.Add(Text("界面主题", 11, Muted));
        side.Children.Add(themeSelector = Combo(["深色", "浅色"], settings.LightTheme ? 1 : 0, i => { Change(() => settings.LightTheme = i == 1); StudioTheme.Apply(settings.LightTheme); SettingsStore.Save(settingsFile, settings); }));
        side.Children.Add(Text("关闭窗口时", 11, Muted));
        side.Children.Add(closeSelector = Combo(["后台运行（系统托盘）", "直接退出"], settings.TrayOnClose ? 0 : 1, i => { Change(() => settings.TrayOnClose = i == 0); SettingsStore.Save(settingsFile, settings); }));
        side.Children.Add(Btn("保存当前设置", Save)); side.Children.Add(Btn("退出软件", () => { closing = true; Close(); }));
        var area = new Grid { Margin = new Thickness(28, 23, 28, 15) }; Grid.SetColumn(area, 1); root.Children.Add(area);
        area.RowDefinitions.Add(new() { Height = GridLength.Auto }); area.RowDefinitions.Add(new()); area.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var heading = new StackPanel(); heading.Children.Add(Text("让数字，有自己的颜色。", 27)); heading.Children.Add(Text("自由配色 / 空间分布 / 动画节奏", 12, Muted)); area.Children.Add(heading);
        var scroll = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 20, 0, 8) }; Grid.SetRow(scroll, 1); area.Children.Add(scroll);
        var foot = new StackPanel(); foot.Children.Add(notice); foot.Children.Add(status); Grid.SetRow(foot, 2); area.Children.Add(foot);
        preview.SegmentClicked = i => selected.Text = $"逻辑段 {i} · {ZoneName(PanelLayout.Zone(i))} · 颜色 {preview.Colors[i].Hex}";
        ShowPage("colors"); if (!args.Contains("--render")) UpdateBridge(); sensors.ReadMotherboard = settings.ReadMotherboard; sensors.Start(); timer.Tick += Tick; timer.Start();
        Loaded += OnLoaded;
        Closing += (_, e) => { if (!closing && settings.TrayOnClose && !args.Contains("--render")) { e.Cancel = true; SettingsStore.Save(settingsFile, settings); SetupTray(); Hide(); return; } Cleanup(); };
    }
    static Brush Hex(string hex) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
    static TextBlock Text(string text, double size = 13, Brush? brush = null) => new() { Text = text, FontSize = size, Foreground = brush ?? Ink, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 5) };
    void ApplyStyles()
    {
        var button = new Style(typeof(Button)); button.Setters.Add(new Setter(Control.BackgroundProperty, StudioTheme.Button)); button.Setters.Add(new Setter(Control.ForegroundProperty, Ink)); button.Setters.Add(new Setter(Control.BorderBrushProperty, StudioTheme.Border)); button.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(12, 9, 12, 9))); button.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, 4, 8, 4))); button.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Left)); Resources.Add(typeof(Button), button);
        var input = new Style(typeof(TextBox)); input.Setters.Add(new Setter(Control.BackgroundProperty, StudioTheme.Input)); input.Setters.Add(new Setter(Control.ForegroundProperty, Ink)); input.Setters.Add(new Setter(Control.BorderBrushProperty, StudioTheme.Border)); input.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(7, 6, 7, 6))); input.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, 3, 7, 3))); Resources.Add(typeof(TextBox), input);
        var check = new Style(typeof(CheckBox)); check.Setters.Add(new Setter(Control.ForegroundProperty, Ink)); check.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, 7, 12, 7))); Resources.Add(typeof(CheckBox), check);
        var combo = new Style(typeof(ComboBox)); combo.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.Black)); combo.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6))); combo.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, 4, 8, 4))); Resources.Add(typeof(ComboBox), combo);
    }
    Button Btn(string caption, Action click) { var b = new Button { Content = caption }; b.Click += (_, _) => Guard(click); return b; }
    void Guard(Action action) { try { action(); } catch (Exception ex) { notice.Text = ex.Message; } }
    Border Card(UIElement child) => new() { Background = Surface, CornerRadius = new CornerRadius(12), Padding = new Thickness(20), Margin = new Thickness(0, 0, 0, 16), Child = child };
    static StackPanel Row(params UIElement[] children) { var row = new StackPanel { Orientation = Orientation.Horizontal }; foreach (var child in children) row.Children.Add(child); return row; }
    void Change(Action update, bool refresh = false)
    {
        if (rebuilding) return;
        string before = SettingsStore.Serialize(settings);
        try { update(); settings.Validate(); } catch { settings = SettingsStore.Deserialize(before); throw; }
        undo.Add(before); if (undo.Count > 80) undo.RemoveAt(0); redo.Clear();
        SyncPreferences(); if (refresh) ShowPage(page);
    }
    void SyncPreferences()
    {
        bool old = rebuilding; rebuilding = true;
        if (themeSelector is not null) themeSelector.SelectedIndex = settings.LightTheme ? 1 : 0;
        if (closeSelector is not null) closeSelector.SelectedIndex = settings.TrayOnClose ? 0 : 1;
        rebuilding = old; StudioTheme.Apply(settings.LightTheme);
        if (!args.Contains("--render") && settings.SignalRgbSync != (signalBridge is not null)) UpdateBridge();
    }
    void Save() { SettingsStore.Save(settingsFile, settings); notice.Text = "已保存配色、效果和传感器选择。"; }
    void ShowPage(string next)
    {
        // Reused controls must leave their old visual parent before rebuilding a page.
        foreach (FrameworkElement element in new FrameworkElement[] { preview, previewState, selected, paletteRows }.Concat(metricTexts)) if (element.Parent is Panel parent) parent.Children.Remove(element);
        rebuilding = true; page = next; content.Children.Clear(); mappingBoxes.Clear(); monitor = null; diagnosticText = null; signalState = null; streamButton = null;
        if (next == "colors") BuildColors(); else if (next == "monitor") BuildMonitor(); else BuildDevice();
        rebuilding = false;
    }
    ComboBox Combo(string[] names, int index, Action<int> changed)
    {
        var box = new ComboBox { ItemsSource = names, SelectedIndex = index, MinWidth = 150 };
        box.SelectionChanged += (_, _) => { if (!rebuilding && box.SelectedIndex >= 0) changed(box.SelectedIndex); }; return box;
    }
    CheckBox Check(string caption, bool value, Action<bool> changed)
    {
        var check = new CheckBox { Content = caption, IsChecked = value }; check.Checked += (_, _) => { if (!rebuilding) changed(true); }; check.Unchecked += (_, _) => { if (!rebuilding) changed(false); }; return check;
    }
    StackPanel SliderField(string title, double value, double min, double max, string suffix, Action<double> update)
    {
        var panel = new StackPanel(); var label = Text($"{title}   {value:0.##}{suffix}", 12, Muted); panel.Children.Add(label);
        var slider = new Slider { Minimum = min, Maximum = max, Value = value, Margin = new Thickness(0, 2, 0, 12) };
        slider.ValueChanged += (_, _) => { if (rebuilding) return; label.Text = $"{title}   {slider.Value:0.##}{suffix}"; Change(() => update(slider.Value)); }; panel.Children.Add(slider); return panel;
    }
    void BuildColors()
    {
        var toolbar = new WrapPanel();
        toolbar.Children.Add(Btn("↶ 撤销", () => History(undo, redo))); toolbar.Children.Add(Btn("↷ 重做", () => History(redo, undo)));
        toolbar.Children.Add(Btn("保存预设…", ExportPreset)); toolbar.Children.Add(Btn("导入预设…", ImportPreset));
        toolbar.Children.Add(Btn("粉紫蓝", () => Preset(["#FFB7D5", "#C9B8FF", "#A9DFFF"]))); toolbar.Children.Add(Btn("日落", () => Preset(["#FF664D", "#FFA853", "#BA78FF"]))); toolbar.Children.Add(Btn("彩虹", () => Preset(["#FF2453", "#FFB52E", "#D4F439", "#28EDC8", "#487AFF", "#C36FFF"])));
        content.Children.Add(toolbar);
        var columns = new Grid { Margin = new Thickness(0, 12, 0, 0) }; columns.ColumnDefinitions.Add(new() { Width = new GridLength(1.2, GridUnitType.Star) }); columns.ColumnDefinitions.Add(new() { Width = new GridLength(20) }); columns.ColumnDefinitions.Add(new()); content.Children.Add(columns);
        var left = new StackPanel(); columns.Children.Add(left); var right = new StackPanel(); Grid.SetColumn(right, 2); columns.Children.Add(right);
        var panel = new StackPanel(); panel.Children.Add(Text("面板预览", 18)); panel.Children.Add(previewState); panel.Children.Add(preview); panel.Children.Add(selected);
        panel.Children.Add(Text("LF8 数字段布局 · 面板预览示意", 11, Muted));
        var previewControls = Row(Btn(paused ? "▶ 继续预览" : "Ⅱ 暂停预览", () => { paused = !paused; ShowPage("colors"); }), Check("示例读数（模拟）", settings.Simulated, v => Change(() => settings.Simulated = v)));
        panel.Children.Add(previewControls); left.Children.Add(Card(panel));
        if (paused) panel.Children.Add(SliderField("预览时间", animationTime % settings.Period, 0, settings.Period, " 秒", v => animationTime = v));
        var metrics = new Grid(); for (int i = 0; i < 4; i++) { metrics.ColumnDefinitions.Add(new()); var p = new StackPanel(); p.Children.Add(Text(new[] { "温度", "功率", "核心频率", "负载" }[i], 11, Muted)); p.Children.Add(metricTexts[i]); Grid.SetColumn(p, i); metrics.Children.Add(p); } left.Children.Add(Card(metrics));
        var display = new StackPanel(); display.Children.Add(Text("数字显示", 17));
        display.Children.Add(Row(Combo(["CPU", "GPU"], settings.Source == "CPU" ? 0 : 1, i => Change(() => settings.Source = i == 0 ? "CPU" : "GPU")), Check("轮播", settings.Rotate, v => Change(() => settings.Rotate = v)), Check("°F", settings.Fahrenheit, v => Change(() => settings.Fahrenheit = v))));
        display.Children.Add(SliderField("轮播间隔", settings.RotateSeconds, 1, 60, " 秒", v => settings.RotateSeconds = v)); display.Children.Add(Text("默认 CPU 频率为核心 Clock 平均值；可在监控页指定传感器。", 11, Muted)); left.Children.Add(Card(display));
        var sync = new StackPanel(); sync.Children.Add(Text("灯光来源", 18));
        sync.Children.Add(Combo(["独立配色", "SignalRGB 同步"], settings.SignalRgbSync ? 1 : 0, i => { Change(() => settings.SignalRgbSync = i == 1, true); SettingsStore.Save(settingsFile, settings); }));
        signalState = Text(BridgeStatus(), 12, Muted); sync.Children.Add(signalState);
        if (settings.SignalRgbSync)
        {
            sync.Children.Add(Text("颜色跟随 SignalRGB；亮度、真实读数和轮播仍由本软件控制。", 12, Muted));
            sync.Children.Add(SliderField("空间平滑 · 高斯加权", settings.SignalRgbSmoothing, 0, 100, "%", v => settings.SignalRgbSmoothing = v));
            sync.Children.Add(Text("0% 保留原始；强度越大，相邻段的色块过渡越柔和。仅处理同步颜色，不影响数字形状。", 11, Muted));
            sync.Children.Add(Check("同步中断两秒后回到独立配色", settings.SignalRgbFallback, v => Change(() => settings.SignalRgbFallback = v)));
            sync.Children.Add(Row(Btn("安装桥接插件", () => { string path = SignalBridge.InstallPlugin(); notice.Text = "插件已安装：" + path + "。请重启 SignalRGB，然后在布局中摆放 Color Studio 面板。"; }), Btn("重启接收", UpdateBridge)));
            sync.Children.Add(Text("插件安装后需重启 SignalRGB；关闭上方回退选项会在等待或中断时熄灭面板。", 11, Muted));
        }
        right.Children.Add(Card(sync));
        var effects = new StackPanel(); effects.Children.Add(Text("空间 × 动画", 18, Accent)); effects.Children.Add(Text("空间样式", 12, Muted));
        effects.Children.Add(Combo(["线性渐变", "径向渐变", "镜像渐变", "环绕渐变", "条纹拼色", "区域拼色", "单色常亮"], (int)settings.Pattern, i => Change(() => settings.Pattern = (Pattern)i, true)));
        effects.Children.Add(Text("动画方式", 12, Muted)); effects.Children.Add(Combo(["静止", "循环流动", "来回流动", "整体颜色过渡", "呼吸", "扫光", "波纹", "温度联动", "负载联动"], (int)settings.Motion, i => Change(() => settings.Motion = (Motion)i, true)));
        effects.Children.Add(SliderField("方向角度 · 0° 向右 / 90° 向下", settings.Angle, 0, 359, "°", v => settings.Angle = v));
        effects.Children.Add(SliderField("动画周期", settings.Period, .5, 60, " 秒", v => settings.Period = v)); effects.Children.Add(SliderField("整体亮度", settings.Brightness, 0, 100, "%", v => settings.Brightness = v));
        effects.Children.Add(Check("反向动画", settings.ReverseMotion, v => Change(() => settings.ReverseMotion = v)));
        effects.Children.Add(Row(Check("硬边拼色", settings.HardEdges, v => Change(() => settings.HardEdges = v)), Check("反转配色", settings.Reverse, v => Change(() => settings.Reverse = v)), Check("点亮", settings.Enabled, v => Change(() => settings.Enabled = v))));
        if (settings.Pattern is Pattern.Radial or Pattern.Conic || settings.Motion == Motion.Ripple)
        { effects.Children.Add(SliderField("中心 X", settings.CenterX, 0, 1, "", v => settings.CenterX = v)); effects.Children.Add(SliderField("中心 Y", settings.CenterY, 0, 1, "", v => settings.CenterY = v)); }
        if (settings.Pattern == Pattern.Stripes || settings.Motion == Motion.Ripple) effects.Children.Add(SliderField("重复次数", settings.Repeats, 1, 12, "", v => settings.Repeats = v));
        if (settings.Motion is Motion.Temperature or Motion.Load)
        {
            effects.Children.Add(Text("联动沿自选色板变化，超出阈值保持端点色。", 11, Muted));
            var lo = new TextBox { Text = settings.LinkLow.ToString("0.##"), Width = 65 }; var hi = new TextBox { Text = settings.LinkHigh.ToString("0.##"), Width = 65 };
            effects.Children.Add(Row(lo, Text("→"), hi, Btn("设置阈值", () => { if (!double.TryParse(lo.Text, out var low) || !double.TryParse(hi.Text, out var high) || high <= low) throw new Exception("请输入有效的下限和上限。"); Change(() => { settings.LinkLow = low; settings.LinkHigh = high; }); })));
        }
        right.Children.Add(Card(effects));
        var palette = new StackPanel(); palette.Children.Add(Text("自选色板", 18)); palette.Children.Add(Text("点击色块打开色轮；HEX 可直接编辑，位置决定渐变占比。", 11, Muted)); paletteRows.Children.Clear(); palette.Children.Add(paletteRows); BuildPalette();
        palette.Children.Add(Row(Btn("＋ 添加颜色", () => { if (settings.Palette.Count >= 16) throw new Exception("首版最多 16 个色标。"); Change(() => { settings.Palette.Add(new("#FFFFFF", 1)); EvenStops(); }, true); }), Btn("均匀分布", () => Change(EvenStops, true))));
        right.Children.Add(Card(palette));
        if (settings.Pattern == Pattern.Zones)
        {
            var zones = new StackPanel(); zones.Children.Add(Text("区域拼色", 18));
            foreach (string key in settings.ZoneColors.Keys.ToArray()) { var k = key; var b = Btn(ZoneName(k) + "   " + settings.ZoneColors[k], () => PickColor(settings.ZoneColors[k], hex => Change(() => settings.ZoneColors[k] = hex, true))); b.BorderBrush = Hex(settings.ZoneColors[k]); zones.Children.Add(b); } right.Children.Add(Card(zones));
        }
    }
    void BuildPalette()
    {
        var header = new Grid(); header.ColumnDefinitions.Add(new() { Width = new GridLength(48) }); header.ColumnDefinitions.Add(new()); header.ColumnDefinitions.Add(new() { Width = new GridLength(68) });
        foreach (var pair in new[] { ("颜色", 0), ("HEX", 1), ("位置 %", 2) }) { var label = Text(pair.Item1, 11, Muted); Grid.SetColumn(label, pair.Item2); header.Children.Add(label); } paletteRows.Children.Add(header);
        for (int i = 0; i < settings.Palette.Count; i++)
        {
            int index = i; var stop = settings.Palette[i];
            var block = new Grid { Margin = new Thickness(0, 9, 0, 9) };
            foreach (double width in new[] { 48d, 1d, 68d }) block.ColumnDefinitions.Add(new() { Width = width == 1 ? new GridLength(1, GridUnitType.Star) : new GridLength(width) });
            block.RowDefinitions.Add(new() { Height = GridLength.Auto }); block.RowDefinitions.Add(new() { Height = GridLength.Auto });
            var swatch = Btn("", () => PickColor(settings.Palette[index].Hex, hex => Change(() => settings.Palette[index] = settings.Palette[index] with { Hex = hex }, true)));
            swatch.Background = Hex(stop.Hex); swatch.Width = 38; swatch.Height = 38; swatch.Padding = new Thickness(0); swatch.ToolTip = "打开色轮与方形取色窗";
            block.Children.Add(swatch);
            var hexBox = new TextBox { Text = stop.Hex, MinWidth = 82, VerticalContentAlignment = VerticalAlignment.Center };
            Grid.SetColumn(hexBox, 1); block.Children.Add(hexBox);
            var position = new TextBox { Text = (stop.Position * 100).ToString("0.#"), VerticalContentAlignment = VerticalAlignment.Center, ToolTip = "色标位置（%）" };
            Grid.SetColumn(position, 2); block.Children.Add(position);
            void Commit()
            {
                var color = Rgb.Parse(hexBox.Text.Trim().ToUpperInvariant());
                if (!double.TryParse(position.Text, out var pos) || pos is < 0 or > 100) throw new Exception("色标位置应为 0–100%。");
                Change(() => settings.Palette[index] = new(color.Hex, pos / 100), true);
            }
            hexBox.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) Guard(Commit); };
            position.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) Guard(Commit); };
            var actions = new WrapPanel(); actions.Children.Add(Btn("应用", Commit)); actions.Children.Add(Btn("↑", () => MoveStop(index, -1))); actions.Children.Add(Btn("↓", () => MoveStop(index, 1)));
            actions.Children.Add(Btn("删除", () => { if (settings.Palette.Count == 1) throw new Exception("至少保留一种颜色。"); Change(() => { settings.Palette.RemoveAt(index); EvenStops(); }, true); }));
            Grid.SetRow(actions, 1); Grid.SetColumnSpan(actions, 3); block.Children.Add(actions); paletteRows.Children.Add(block);
        }
    }
    void EvenStops() { for (int i = 0; i < settings.Palette.Count; i++) settings.Palette[i] = settings.Palette[i] with { Position = settings.Palette.Count == 1 ? 0 : (double)i / (settings.Palette.Count - 1) }; }
    void MoveStop(int i, int delta) { int j = i + delta; if (j < 0 || j >= settings.Palette.Count) return; Change(() => { var a = settings.Palette[i]; var b = settings.Palette[j]; settings.Palette[i] = new(b.Hex, a.Position); settings.Palette[j] = new(a.Hex, b.Position); }, true); }
    void Preset(string[] colors) => Change(() => { settings.Palette = colors.Select((c, i) => new ColorStop(c, (double)i / (colors.Length - 1))).ToList(); }, true);
    void History(List<string> from, List<string> to) { if (from.Count == 0) return; to.Add(SettingsStore.Serialize(settings)); settings = SettingsStore.Deserialize(from[^1]); from.RemoveAt(from.Count - 1); SyncPreferences(); ShowPage(page); }
    void PickColor(string old, Action<string> apply)
    {
        var dialog = new ColorPickerWindow(old) { Owner = this };
        if (dialog.ShowDialog() == true) apply(dialog.SelectedHex);
    }
    void ExportPreset() { var d = new SaveFileDialog { Filter = "配色预设 (*.json)|*.json", FileName = "我的配色.json" }; if (d.ShowDialog(this) == true) { SettingsStore.Save(d.FileName, settings); notice.Text = "预设已导出。"; } }
    void ImportPreset() { var d = new OpenFileDialog { Filter = "配色预设 (*.json)|*.json" }; if (d.ShowDialog(this) == true) { var loaded = SettingsStore.Load(d.FileName); Change(() => settings = loaded, true); notice.Text = "已导入预设；点击保存当前设置可用于下次启动。"; } }
    static string ZoneName(string z) => z switch { "Temperature" => "温度", "Power" => "功率", "Clock" => "频率", "Load" => "负载", _ => "CPU / GPU 标签" };
    void BuildMonitor()
    {
        var top = new StackPanel(); top.Children.Add(Text("硬件监控", 21)); top.Children.Add(Text("以下均为本机实测传感器。无法读取显示 —；主板风扇读取暂未启用，避免自动加载主板访问驱动。", 12, Muted));
        monitorFilter = Combo(["全部", "CPU", "GPU", "Memory", "Storage", "Network", "Fan / Mainboard"], 0, _ => RefreshMonitor()); top.Children.Add(Row(monitorFilter, Btn("导出诊断…", ExportDiagnostic))); content.Children.Add(Card(top));
        monitor = new DataGrid { Height = 340, IsReadOnly = true, AutoGenerateColumns = false, CanUserAddRows = false, Background = StudioTheme.Sidebar, Foreground = Ink, RowBackground = StudioTheme.Table, AlternatingRowBackground = StudioTheme.TableAlternate, GridLinesVisibility = DataGridGridLinesVisibility.None, HeadersVisibility = DataGridHeadersVisibility.Column, SelectionMode = DataGridSelectionMode.Single };
        foreach (var column in new[] { ("设备", "Hardware", 2.0), ("传感器", "Name", 1.7), ("类型", "Kind", 1.0), ("读数", "Display", 1.0), ("说明", "Reason", 2.0) }) monitor.Columns.Add(new DataGridTextColumn { Header = column.Item1, Binding = new System.Windows.Data.Binding(column.Item2), Width = new DataGridLength(column.Item3, DataGridLengthUnitType.Star) });
        content.Children.Add(monitor);
        var mapping = new StackPanel { Margin = new Thickness(0, 18, 0, 0) }; mapping.Children.Add(Text("面板的数据来源", 18)); mapping.Children.Add(Text("自动候选：CPU Package 温度/功率、总负载；GPU Core 温度/负载、Board/Package 功率。多设备时需手动选择。失效的已选传感器不会静默替换。", 12, Muted));
        foreach (string source in new[] { "CPU", "GPU" }) foreach (string kind in new[] { "Temperature", "Power", "Clock", "Load" })
        {
            string key = source + "." + kind; var box = new ComboBox { MinWidth = 550 }; box.SelectionChanged += (_, _) => { if (rebuilding || box.SelectedItem is not SensorChoice choice) return; Change(() => { if (choice.Id == "") settings.SensorMappings.Remove(key); else settings.SensorMappings[key] = choice.Id; }); };
            mappingBoxes[key] = box; mapping.Children.Add(Row(Text(source + " " + ZoneName(kind), 12), box));
        }
        content.Children.Add(Card(mapping)); mappingSignature = ""; RefreshMonitor(); RefreshMapping();
    }
    record SensorChoice(string Id, string Label) { public override string ToString() => Label; }
    void RefreshMonitor()
    {
        if (monitor is null) return; var filter = monitorFilter?.SelectedItem?.ToString() ?? "全部";
        monitor.ItemsSource = sensors.Snapshot.Where(r => filter == "全部" || r.Group == filter).ToArray();
    }
    void RefreshMapping()
    {
        if (mappingBoxes.Count == 0) return;
        string signature = string.Join('|', sensors.Snapshot.Select(r => r.Id)); if (signature == mappingSignature) return; mappingSignature = signature;
        bool old = rebuilding; rebuilding = true;
        foreach (var (key, box) in mappingBoxes)
        {
            var parts = key.Split('.'); var choices = new List<SensorChoice> { new("", "自动候选" + (parts[1] == "Clock" ? "（CPU 核心频率平均值 / GPU Core）" : "")) };
            choices.AddRange(sensors.Snapshot.Where(r => r.Group == parts[0] && r.Kind == parts[1]).Select(r => new SensorChoice(r.Id, $"{r.Hardware} / {r.Name} [{r.Unit}]")));
            if (settings.SensorMappings.TryGetValue(key, out var id) && choices.All(c => c.Id != id)) choices.Add(new(id, "已选传感器失效：" + id));
            box.ItemsSource = choices; box.SelectedItem = choices.FirstOrDefault(c => c.Id == id) ?? choices[0];
        }
        rebuilding = old;
    }
    void BuildDevice()
    {
        var p = new StackPanel(); p.Children.Add(Text("设备连接与实机验证", 21)); p.Children.Add(Text("启动时自动识别受支持的 LF8 设备并显示；也可以手动连接或运行测试。", 12, Muted));
        deviceSelect = new ComboBox { MinWidth = 700 }; RefreshDevices(); p.Children.Add(deviceSelect);
        p.Children.Add(Row(Btn("刷新设备", RefreshDevices), Btn("连接 / 验证握手", () => _ = Connect()), Btn("断开", () => { streaming = false; testStop?.Cancel(); try { writeTask?.Wait(2000); } catch { } hid.Dispose(); UpdateDiagnostic(); })));
        
        p.Children.Add(Row(Btn("10% 亮度单色测试", () => _ = TestColor()), Btn("10% 区域拼色测试", () => _ = TestZones())));
        streamButton = Btn(streaming ? "■ 停止实时输出" : "▶ 开始实时输出", ToggleStreaming); p.Children.Add(streamButton);
        p.Children.Add(Text("实时输出使用真实传感器；示例读数仅用于软件预览。缺失或过期指标在面板留空。断线后停止，可点连接重新识别并恢复输出。", 12, Muted)); content.Children.Add(Card(p));
        var diag = new StackPanel(); diag.Children.Add(Text("诊断记录", 18)); diagnosticText = new TextBox { IsReadOnly = true, AcceptsReturn = true, Height = 220, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; diag.Children.Add(diagnosticText); diag.Children.Add(Btn("导出设备与传感器诊断…", ExportDiagnostic)); content.Children.Add(Card(diag));
        var options = new StackPanel(); options.Children.Add(Text("运行设置", 18)); options.Children.Add(Check("启动后自动连接并显示", settings.AutoStartOutput, v => { Change(() => settings.AutoStartOutput = v); SettingsStore.Save(settingsFile, settings); }));
        options.Children.Add(Text("关闭行为与浅色/深色主题可在左侧切换。后台运行位于任务栏右侧系统托盘，双击图标可恢复窗口。", 12, Muted));
        options.Children.Add(Check("读取主板温度和风扇 RPM（只读，可能需管理员权限）", settings.ReadMotherboard, v => Change(() => { settings.ReadMotherboard = v; sensors.ReadMotherboard = v; })));
        options.Children.Add(Text(sensors.DriverStatus, 12, Muted)); options.Children.Add(Text("启动后自动连接仅作用于受支持的设备。本版不设置开机启动。托盘后台运行会继续更新面板，完全退出后停止写入。", 12, Muted)); options.Children.Add(Btn("恢复默认配色", () => Change(() => settings = new(), true))); content.Children.Add(Card(options)); UpdateDiagnostic();
    }
    void RefreshDevices()
    {
        if (deviceSelect is null) return; var list = HidService.Enumerate().Where(d => d.Vid == 0x0416 && d.Pid == 0x8001).ToArray(); deviceSelect.ItemsSource = list.Select(d => d.Path).ToArray(); if (list.Length > 0) deviceSelect.SelectedIndex = 0;
    }
    async Task Connect()
    {
        if (connecting || writeTask is { IsCompleted: false }) return;
        connecting = true;
        streaming = false; layoutAllowed = false;
        try { string? path = deviceSelect?.SelectedItem as string; await Task.Run(() => hid.Connect(path)); layoutAllowed = hid.Supported; notice.Text = hid.Status; }
        catch (Exception ex) { notice.Text = ex.Message; }
        finally { connecting = false; }
        if (layoutAllowed) StartStreaming();
        UpdateDiagnostic();
    }
    bool CanWrite()
    {
        if (connecting || !hid.Connected || !hid.Supported) { notice.Text = "请先成功握手受支持的设备。"; return false; }
        return layoutAllowed;
    }
    async Task TestColor() => await TestOutput(false);
    async Task TestZones() => await TestOutput(true);
    async Task TestOutput(bool zones)
    {
        if (!CanWrite() || writeTask is { IsCompleted: false }) return; streaming = false;
        var test = SettingsStore.Deserialize(SettingsStore.Serialize(settings)); test.Brightness = 10; test.Pattern = zones ? Pattern.Zones : Pattern.Solid; test.Motion = Motion.Still; test.Enabled = true;
        var frame = PanelLayout.Frame(test, new(38, 47, 4489, 25), "CPU", 0);
        testStop?.Dispose(); testStop = new(); var token = testStop.Token;
        try { notice.Text = "持续显示测试图案 20 秒：CPU 38°C / 47W / 4489MHz / 25%，这是测试数字。"; writeTask = Task.Run(() => { for (int i = 0; i < 100 && !token.IsCancellationRequested; i++) { hid.Send(frame); Thread.Sleep(170); } }); await writeTask; notice.Text = "20 秒测试已结束；可以开始实时输出。"; }
        catch (Exception ex) { notice.Text = ex.Message; }
    }
    void ToggleStreaming()
    {
        if (streaming) { streaming = false; if (streamButton is not null) streamButton.Content = "▶ 开始实时输出"; return; }
        if (!CanWrite()) return; StartStreaming();
    }
    void StartStreaming()
    {
        streaming = true; previousOutput = null; settings.Simulated = false; if (streamButton is not null) streamButton.Content = "■ 停止实时输出"; notice.Text = "实时输出已开始。关闭窗口可按左侧选项继续后台运行。";
    }
    object Diagnostic() => new { Version = "0.3.4", Time = DateTimeOffset.Now, OS = Environment.OSVersion.ToString(), Hid = HidService.Enumerate(), hid.Pm, hid.Sub, hid.Status, hid.RawHandshake, Sensors = sensors.Snapshot, SensorError = sensors.Error, sensors.DriverStatus, FramesSent = frames, SignalRgb = new { Status = BridgeStatus(), Received = signalBridge?.Received, Rejected = signalBridge?.Rejected }, TrccRunning = HidService.TrccRunning };
    void ExportDiagnostic() { var d = new SaveFileDialog { Filter = "诊断 JSON (*.json)|*.json", FileName = "Thermalright-diagnostic.json" }; if (d.ShowDialog(this) == true) { File.WriteAllText(d.FileName, JsonSerializer.Serialize(Diagnostic(), SettingsStore.Json)); notice.Text = "诊断已导出，包含设备路径和传感器 ID。"; } }
    void UpdateDiagnostic() { if (diagnosticText is not null) diagnosticText.Text = $"状态：{hid.Status}\n设备路径：{hid.DevicePath}\nPM={hid.Pm} SUB={hid.Sub}\n原始握手：{hid.RawHandshake}\n传感器：{sensors.Snapshot.Length} 项\n传感器错误：{sensors.Error}\n固件可能每次上电只响应一次握手；无有效响应时停止，不猜测型号。\n"; }
    void Tick(object? sender, EventArgs e)
    {
        double elapsed = clock.Elapsed.TotalSeconds; if (!paused) animationTime += elapsed - previousTick; previousTick = elapsed; double time = animationTime;
        string source = settings.Rotate ? (((int)(clock.Elapsed.TotalSeconds / settings.RotateSeconds) % 2 == 0) ? settings.Source : settings.Source == "CPU" ? "GPU" : "CPU") : settings.Source;
        var real = sensors.GetMetrics(source, settings);
        var metrics = settings.Simulated ? new Metrics(47, 36, 2450, 23) : real;
        var frame = OutputFrame(metrics, source, time);
        preview.Colors = frame; preview.Mask = PanelLayout.Mask(metrics, source, settings.Fahrenheit); preview.InvalidateVisual();
        previewState.Text = settings.Simulated ? $"模拟 / 未连接预览 · {source} · 示例读数" : $"本机传感器 · {source} · {(streaming ? "正在发送" : "仅软件预览")}";
        double? temperature = metrics.Temperature.HasValue && settings.Fahrenheit ? metrics.Temperature * 1.8 + 32 : metrics.Temperature;
        double?[] values = [temperature, metrics.Power, metrics.Clock, metrics.Load]; string[] units = [settings.Fahrenheit ? "°F" : "°C", "W", "MHz", "%"];
        for (int i = 0; i < 4; i++) metricTexts[i].Text = values[i].HasValue ? $"{values[i]:0} {units[i]}" : "—";
        if (streaming && (writeTask is null || writeTask.IsCompleted))
        {
            if (writeTask?.IsFaulted == true) { streaming = false; notice.Text = "写入停止：" + writeTask.Exception!.GetBaseException().Message; hid.Dispose(); }
            else { var output = OutputFrame(real, source, clock.Elapsed.TotalSeconds); if (previousOutput is null || !previousOutput.SequenceEqual(output) || elapsed - lastOutput >= .2) { previousOutput = output; lastOutput = elapsed; writeTask = Task.Run(() => { hid.Send(output); Interlocked.Increment(ref frames); }); } }
        }
        if (clock.Elapsed.TotalSeconds - lastSample >= 1)
        {
            lastSample = clock.Elapsed.TotalSeconds; RefreshMonitor(); RefreshMapping(); if (signalState is not null) signalState.Text = BridgeStatus();
            status.Text = $"{hid.Status}   |   传感器 {sensors.Snapshot.Length} 项   |   {(streaming ? "实时输出 · 目标 20 FPS" : "设备输出已停止")}   |   已提交 {frames} 帧";
        }
    }
    string BridgeStatus() => !settings.SignalRgbSync ? "使用自选色板与本地动画" : bridgeError.Length > 0 ? bridgeError : signalBridge?.Status ?? "同步接收未启动";
    void UpdateBridge()
    {
        signalBridge?.Dispose(); signalBridge = null; bridgeError = "";
        if (!settings.SignalRgbSync) return;
        try { signalBridge = new SignalBridge(); signalBridge.Start(); }
        catch (Exception ex) { bridgeError = "同步接收未启动：" + ex.Message; signalBridge?.Dispose(); signalBridge = null; notice.Text = bridgeError; }
    }
    Rgb[] OutputFrame(Metrics metrics, string source, double time)
    {
        if (settings.SignalRgbSync)
        {
            if (signalBridge?.Fresh == true && signalBridge.Latest is { } frame) return SignalBridge.MaskFrame(frame.Colors, settings, metrics, source);
            if (!settings.SignalRgbFallback) return new Rgb[93];
        }
        return PanelLayout.Frame(settings, metrics, source, time);
    }
    void SetupTray()
    {
        if (tray is not null) return;
        using var iconStream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("ThermalrightColorStudio.StudioIcon.ico") ?? throw new IOException("应用图标资源缺失。");
        using var sourceIcon = new System.Drawing.Icon(iconStream);
        tray = new System.Windows.Forms.NotifyIcon { Icon = (System.Drawing.Icon)sourceIcon.Clone(), Text = "Thermalright Color Studio", Visible = true };
        var menu = new System.Windows.Forms.ContextMenuStrip(); menu.Items.Add("打开配色工作室", null, (_, _) => Dispatcher.Invoke(() => { Show(); Activate(); })); menu.Items.Add("退出", null, (_, _) => Dispatcher.Invoke(() => { closing = true; Close(); })); tray.ContextMenuStrip = menu; tray.DoubleClick += (_, _) => Dispatcher.Invoke(() => { Show(); Activate(); });
    }
    void Cleanup()
    {
        timer.Stop(); streaming = false; signalBridge?.Dispose(); testStop?.Cancel(); try { writeTask?.Wait(TimeSpan.FromSeconds(2)); } catch { } hid.Dispose(); sensors.Dispose(); if (tray is not null) { tray.Visible = false; tray.Icon?.Dispose(); tray.Dispose(); }
        try { SettingsStore.Save(settingsFile, settings); } catch { }
    }
    static void Capture(FrameworkElement element, string path)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32); bitmap.Render(element);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var file = File.Create(path); encoder.Save(file);
    }
    async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!args.Contains("--render"))
        {
            if (settings.AutoStartOutput) await Connect();
            if (args.Contains("--verify-startup"))
            {
                string resultPath = args[Array.IndexOf(args, "--verify-startup") + 1];
                try
                {
                    var wait = Stopwatch.StartNew(); while (sensors.Snapshot.Length == 0 && wait.Elapsed.TotalSeconds < 35) await Task.Delay(500);
                    await Task.Delay(3000);
                    if (!streaming || frames < 5) throw new Exception("自动连接或实时输出未启动：" + hid.Status);
                    bool oldClose = settings.TrayOnClose; settings.TrayOnClose = true; Close();
                    if (IsVisible || tray is null || !tray.Visible || !streaming) throw new Exception("关闭转入托盘失败。");
                    long before = frames; await Task.Delay(1500); if (frames <= before) throw new Exception("托盘后台输出未继续。");
                    Show(); Activate(); settings.TrayOnClose = oldClose;
                    File.WriteAllText(resultPath, JsonSerializer.Serialize(new { Passed = true, AutoConnected = hid.Connected, BackgroundOutput = true, Frames = frames, Cpu = sensors.GetMetrics("CPU", settings), Gpu = sensors.GetMetrics("GPU", settings) }, SettingsStore.Json));
                }
                catch (Exception ex) { File.WriteAllText(resultPath, ex.ToString()); }
            }
            if (args.Contains("--verify-sync"))
            {
                string resultPath = args[Array.IndexOf(args, "--verify-sync") + 1];
                var wait = Stopwatch.StartNew(); double freshSince = -1;
                while (wait.Elapsed.TotalSeconds < 600 && !closing)
                {
                    if (signalBridge?.Fresh == true) { if (freshSince < 0) freshSince = wait.Elapsed.TotalSeconds; }
                    else freshSince = -1;
                    bool passed = freshSince >= 0 && wait.Elapsed.TotalSeconds - freshSince >= 5 && streaming && frames > 10;
                    File.WriteAllText(resultPath, JsonSerializer.Serialize(new { Time = DateTimeOffset.Now, Passed = passed, Streaming = streaming, Frames = frames, SyncStatus = BridgeStatus(), Received = signalBridge?.Received, Rejected = signalBridge?.Rejected, Fresh = signalBridge?.Fresh, Cpu = sensors.GetMetrics("CPU", settings), Gpu = sensors.GetMetrics("GPU", settings) }, SettingsStore.Json));
                    if (passed) break;
                    await Task.Delay(1000);
                }
            }
            return;
        }
        await Task.Delay(1800); string path = args[Array.IndexOf(args, "--render") + 1];
        try
        {
            if (File.Exists(path + ".error.txt")) File.Delete(path + ".error.txt");
            // Exercise page navigation and real selection handlers before rendering.
            var initial = SettingsStore.Serialize(settings);
            foreach (string target in new[] { "monitor", "device", "colors", "monitor", "colors" }) ShowPage(target);
            Change(() => { settings.Pattern = Pattern.Zones; settings.Motion = Motion.Breathe; }, true);
            Change(() => { settings.Palette.Add(new("#123456", 1)); EvenStops(); }, true);
            History(undo, redo); History(redo, undo);
            settings = SettingsStore.Deserialize(initial); ShowPage("colors");
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(path)!, "ui-smoke-results.txt"), "PASS 页面往返、区域效果、色板添加、撤销/重做、预览重建\n");
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(path)!, "test-results.txt"), SelfTests.Run());
            var wheelTest = new HueSquare(new Rgb(255, 0, 0));
            if (!wheelTest.SelectAt(new Point(200, 60), true) || wheelTest.Color.Hex != "#FF0000" || !wheelTest.SelectAt(new Point(60, 60), true) || wheelTest.Color.Hex != "#FFFFFF" || !wheelTest.SelectAt(new Point(200, 200), true) || wheelTest.Color != default || wheelTest.SelectAt(new Point(1, 1), true)) throw new Exception("色轮方形点击测试失败。");
            wheelTest.SelectAt(new Point(242, 130), true); wheelTest.SelectAt(new Point(200, 60), true);
            if (wheelTest.Color.Hex != "#FF0000") throw new Exception("色环点击测试失败。");
            settings.Motion = Motion.Still; Tick(null, EventArgs.Empty); UpdateLayout();
            string folder = Path.GetDirectoryName(path)!;
            foreach (bool light in new[] { false, true })
            {
                settings.LightTheme = light; SyncPreferences(); UpdateLayout();
                string theme = light ? "light" : "dark";
                Capture(this, Path.Combine(folder, "studio-" + theme + ".png"));
                var parent = (Panel)paletteRows.Parent; parent.Children.Remove(paletteRows);
                double paletteWidth = 410; paletteRows.Measure(new Size(paletteWidth, double.PositiveInfinity)); paletteRows.Arrange(new Rect(0, 0, paletteWidth, paletteRows.DesiredSize.Height)); paletteRows.UpdateLayout();
                Capture(paletteRows, Path.Combine(folder, "palette-" + theme + ".png")); parent.Children.Add(paletteRows); UpdateLayout();
                var pickerWindow = new ColorPickerWindow("#FF664D") { Owner = this, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = 0 };
                pickerWindow.Show(); pickerWindow.UpdateLayout(); Capture(pickerWindow, Path.Combine(folder, "picker-" + theme + ".png")); pickerWindow.Close();
            }
            settings = SettingsStore.Deserialize(initial); SyncPreferences(); ShowPage("colors"); Tick(null, EventArgs.Empty); UpdateLayout();
            File.AppendAllText(Path.Combine(folder, "ui-smoke-results.txt"), "PASS 深浅主题、色板对齐、内置取色器渲染、色轮与方形命中/端点\n");
            var bitmap = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(this);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var file = File.Create(path); encoder.Save(file);
        }
        catch (Exception ex) { File.WriteAllText(path + ".error.txt", ex.ToString()); }
        finally { closing = true; Close(); }
    }
}


