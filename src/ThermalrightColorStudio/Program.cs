using System.IO;
using System.Text.Json;
using System.Windows;

namespace ThermalrightColorStudio;
public static class Program
{
    [STAThread] public static int Main(string[] args)
    {
        // EXE and optional launcher share the same portable settings directory.
        SettingsStore.Root = Path.Combine(AppContext.BaseDirectory, "data");
        var dataIndex = Array.IndexOf(args, "--data-dir");
        if (dataIndex >= 0) SettingsStore.Root = Path.GetFullPath(args[dataIndex + 1]);
        try
        {
            if (args.Contains("--bridge-check"))
            {
                string path = args[Array.IndexOf(args, "--bridge-check") + 1];
                using var bridge = new SignalBridge(); bridge.Start();
                if (args.Contains("--install-plugin")) SignalBridge.InstallPlugin();
                string? stopFile = args.Contains("--stop-file") ? args[Array.IndexOf(args, "--stop-file") + 1] : null;
                var wait = System.Diagnostics.Stopwatch.StartNew();
                while (wait.Elapsed.TotalSeconds < 600 && (stopFile is null || !File.Exists(stopFile)))
                {
                    File.WriteAllText(path, JsonSerializer.Serialize(new { At = DateTimeOffset.Now, bridge.Status, bridge.Received, bridge.Rejected, Fresh = bridge.Fresh, Colors = bridge.Latest?.Colors.Select(c => c.Hex).ToArray() }, SettingsStore.Json)); Thread.Sleep(500);
                }
                return 0;
            }
            if (args.Contains("--self-test")) { var result = SelfTests.Run(); File.WriteAllText(args[Array.IndexOf(args, "--self-test") + 1], result); return 0; }
            if (args.Contains("--probe"))
            {
                using var hid = new HidService(); string error = "";
                try { hid.Connect(); } catch (Exception ex) { error = ex.ToString(); }
                File.WriteAllText(args[Array.IndexOf(args, "--probe") + 1], JsonSerializer.Serialize(new { Hid = HidService.Enumerate(), hid.DevicePath, hid.Pm, hid.Sub, hid.Supported, hid.Status, hid.RawHandshake, Error = error }, SettingsStore.Json)); return error.Length == 0 ? 0 : 1;
            }
            if (args.Contains("--hardware-live-test"))
            {
                string path = args[Array.IndexOf(args, "--hardware-live-test") + 1];
                string? stopFile = args.Contains("--stop-file") ? args[Array.IndexOf(args, "--stop-file") + 1] : null;
                using var sensors = new SensorService(); sensors.Start();
                var wait = System.Diagnostics.Stopwatch.StartNew();
                while (sensors.Snapshot.Length == 0 && wait.Elapsed.TotalSeconds < 35) Thread.Sleep(500);
                using var hid = new HidService(); hid.Connect();
                if (!hid.Supported) throw new IOException("未知布局，未发送测试帧。");
                var settings = new StudioSettings { Pattern = Pattern.Linear, Motion = Motion.Flow, Angle = 35, Period = 6, Brightness = 40, Palette = [new("#FF2476", 0), new("#902BFF", .5), new("#168CFF", 1)] };
                var samples = new List<object>(); int frames = 0;
                var elapsed = System.Diagnostics.Stopwatch.StartNew();
                File.WriteAllText(path + ".running", "真实 CPU/GPU 每两秒轮播，35° 粉紫蓝流动渐变，40% 亮度");
                double duration = args.Contains("--long-observation") ? 600 : 120;
                while (elapsed.Elapsed.TotalSeconds < duration && (stopFile is null || !File.Exists(stopFile)))
                {
                    double time = elapsed.Elapsed.TotalSeconds; string source = (int)(time / 2) % 2 == 0 ? "CPU" : "GPU";
                    var metrics = sensors.GetMetrics(source, settings);
                    hid.Send(PanelLayout.Frame(settings, metrics, source, time));
                    if (frames % 20 == 0) samples.Add(new { At = DateTimeOffset.Now, Source = source, Metrics = metrics });
                    frames++; Thread.Sleep(20);
                    if (frames % 20 == 0) File.WriteAllText(path + ".running", $"{DateTimeOffset.Now:O} | {frames} frames | {source} | {metrics}");
                }
                File.WriteAllText(path, JsonSerializer.Serialize(new { Frames = frames, Seconds = elapsed.Elapsed.TotalSeconds, Samples = samples, hid.Pm, hid.Sub, sensors.DriverStatus, VisualConfirmation = "Pending user observation" }, SettingsStore.Json));
                File.Delete(path + ".running"); return 0;
            }
            if (args.Contains("--hardware-test"))
            {
                using var hid = new HidService(); hid.Connect();
                if (!hid.Supported) throw new IOException("未知布局，未发送测试帧。");
                bool visible = args.Contains("--visible-test");
                var settings = new StudioSettings { Pattern = Pattern.Solid, Brightness = visible ? 30 : 10, Palette = [new(visible ? "#FF8833" : "#A9DFFF", 0)] };
                bool zones = args.Contains("--zone-test");
                if (zones) { settings.Pattern = Pattern.Zones; settings.Brightness = 40; settings.ZoneColors = new() { ["Temperature"] = "#FF0000", ["Power"] = "#00FF00", ["Clock"] = "#0000FF", ["Load"] = "#FF00FF", ["Labels"] = "#00FFFF" }; }
                int count = args.Contains("--hold-test") ? 1200 : 20;
                string? stopFile = args.Contains("--stop-file") ? args[Array.IndexOf(args, "--stop-file") + 1] : null;
                File.WriteAllText(args[Array.IndexOf(args, "--hardware-test") + 1] + ".running", $"PM={hid.Pm} SUB={hid.Sub} · {settings.Brightness}% · {settings.Palette[0].Hex}");
                for (int i = 0; i < count && (stopFile is null || !File.Exists(stopFile)); i++) { hid.Send(PanelLayout.Frame(settings, new(38, 47, 4489, 25), "CPU", 0, visible && !zones)); Thread.Sleep(70); }
                Thread.Sleep(1000);
                File.WriteAllText(args[Array.IndexOf(args, "--hardware-test") + 1], JsonSerializer.Serialize(new { At = DateTimeOffset.Now, hid.Pm, hid.Sub, hid.RawHandshake, settings.Brightness, Color = settings.Palette[0].Hex, TestDigits = visible ? "All segments (test pattern)" : "CPU 38°C / 47W / 4489MHz / 25%", WriteCompleted = true, VisualConfirmation = "Pending user observation" }, SettingsStore.Json)); return 0;
            }
            if (args.Contains("--diagnose"))
            {
                using var sensors = new SensorService(); sensors.Start();
                var wait = System.Diagnostics.Stopwatch.StartNew(); while (sensors.Snapshot.Length == 0 && wait.Elapsed.TotalSeconds < 35) Thread.Sleep(500);
                Thread.Sleep(1500);
                File.WriteAllText(args[Array.IndexOf(args, "--diagnose") + 1], JsonSerializer.Serialize(new { At = DateTimeOffset.Now, OS = Environment.OSVersion.ToString(), Hid = HidService.Enumerate(), Sensors = sensors.Snapshot, sensors.Error, sensors.DriverStatus, TrccRunning = HidService.TrccRunning }, SettingsStore.Json)); return 0;
            }
            using var mutex = new Mutex(true, args.Contains("--render") ? "Local\\ThermalrightColorStudio-Render" : "Local\\ThermalrightColorStudio", out bool first);
            if (!first) { MessageBox.Show("软件已经运行，请查看任务栏或托盘。", "Thermalright Color Studio"); return 1; }
            var app = new System.Windows.Application();
            var window = new MainWindow(args);
            app.Run(window); return 0;
        }
        catch (Exception ex)
        {
            Directory.CreateDirectory(SettingsStore.Root); File.WriteAllText(Path.Combine(SettingsStore.Root, "startup-error.txt"), ex.ToString());
            if (!args.Any(a => a.StartsWith("--"))) MessageBox.Show(ex.Message, "启动失败");
            return 1;
        }
    }
}
