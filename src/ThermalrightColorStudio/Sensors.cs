using LibreHardwareMonitor.Hardware;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Management;

namespace ThermalrightColorStudio;

public record SensorReading(string Id, string Hardware, string Group, string Name, string Kind, double? Value, string Unit, string Reason, DateTimeOffset SampledAt)
{
    public string Display => Value.HasValue ? $"{Value:0.##} {Unit}" : "—";
}
public record Metrics(double? Temperature, double? Power, double? Clock, double? Load);
public sealed class SensorService : IDisposable
{
    readonly Computer computer = new() { IsCpuEnabled = true, IsGpuEnabled = true, IsMemoryEnabled = true, IsStorageEnabled = true, IsNetworkEnabled = false, IsMotherboardEnabled = false, IsControllerEnabled = false };
    readonly CancellationTokenSource stop = new();
    readonly Dictionary<string, (long Tx, long Rx, DateTimeOffset Time, long TotalTx, long TotalRx)> networks = new();
    Task? loop;
    public SensorReading[] Snapshot { get; private set; } = [];
    public string Error { get; private set; } = "";
    public string DriverStatus { get; private set; } = "检查中";
    bool cpuAccess;
    public bool ReadMotherboard { get; set; }
    public void Start() => loop = Task.Run(async () =>
    {
        try
        {
            bool installed = LibreHardwareMonitor.PawnIo.PawnIo.IsInstalled;
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            bool admin = new System.Security.Principal.WindowsPrincipal(identity).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            cpuAccess = installed && admin;
            DriverStatus = !installed ? "未安装 PawnIO：CPU 温度、功率和核心频率不可用。" : !admin ? "PawnIO 已安装：读取 CPU 底层指标需要以管理员身份运行。" : $"PawnIO {LibreHardwareMonitor.PawnIo.PawnIo.Version} · 管理员权限";
            computer.Open();
        }
        catch (Exception ex) { Error = ex.Message; }
        while (!stop.IsCancellationRequested)
        {
            var rows = new List<SensorReading>(); var now = DateTimeOffset.Now;
            try { if (computer.IsMotherboardEnabled != ReadMotherboard) computer.IsMotherboardEnabled = ReadMotherboard; } catch (Exception ex) { Error = ex.Message; }
            foreach (var hw in computer.Hardware) Read(hw, rows, now);
            AddSystem(rows, now);
            AddDisks(rows, now);
            if (!rows.Any(r => r.Group == "Memory" && r.Kind == "Temperature")) rows.Add(new("unavailable/memory/temperature", "内存", "Memory", "内存温度", "Temperature", null, "°C", "未检测到可读取的 DIMM 温度传感器。", now));
            if (!rows.Any(r => r.Group == "Memory" && r.Kind == "Clock")) rows.Add(new("unavailable/memory/clock", "内存", "Memory", "内存时钟", "Clock", null, "MHz", "本机未提供可读取的内存时钟；不使用标称频率代替。", now));
            if (!rows.Any(r => r.Kind == "Fan")) rows.Add(new("unavailable/fans", "主板 / 风扇", "Fan / Mainboard", "CPUFAN / 机箱风扇", "Fan", null, "RPM", ReadMotherboard ? "未检测到测速反馈；需要支持的主板与读取权限。" : "主板传感器读取未启用；可在设备页开启只读监控。", now));
            Snapshot = rows.ToArray();
            try { await Task.Delay(1000, stop.Token); } catch (OperationCanceledException) { break; }
        }
        try { computer.Close(); } catch { }
    });
    void Read(IHardware hw, List<SensorReading> rows, DateTimeOffset now)
    {
        string failure = ""; try { hw.Update(); } catch (Exception ex) { failure = ex.Message; }
        string group = hw.HardwareType switch { HardwareType.Cpu => "CPU", HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel => "GPU", HardwareType.Memory => "Memory", HardwareType.Storage => "Storage", _ => "Fan / Mainboard" };
        foreach (var s in hw.Sensors)
        {
            double? value = failure.Length == 0 && s.Value.HasValue && float.IsFinite(s.Value.Value) ? s.Value.Value : null;
            string reason = value.HasValue ? "" : failure.Length > 0 ? failure : "硬件未提供读数；可能需要权限或不支持此传感器。";
            if (group == "CPU" && !cpuAccess && s.SensorType is SensorType.Temperature or SensorType.Power or SensorType.Clock) { value = null; reason = DriverStatus; }
            rows.Add(new(s.Identifier.ToString(), hw.Name, group, s.Name, s.SensorType.ToString(), value, s.SensorType switch { SensorType.Temperature => "°C", SensorType.Power => "W", SensorType.Clock => "MHz", SensorType.Load or SensorType.Level => "%", SensorType.Fan => "RPM", SensorType.Data => "GiB", SensorType.SmallData => "MiB", SensorType.Throughput => "B/s", SensorType.Voltage => "V", _ => "" }, reason, now));
        }
        foreach (var child in hw.SubHardware) Read(child, rows, now);
    }
    [StructLayout(LayoutKind.Sequential)] struct MemoryStatus { public uint Length, Load; public ulong Total, Available, PageTotal, PageAvailable, VirtualTotal, VirtualAvailable, Extended; }
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    void AddSystem(List<SensorReading> rows, DateTimeOffset now)
    {
        var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        if (GlobalMemoryStatusEx(ref memory))
        {
            rows.Add(new("system/memory/load", "Windows 内存", "Memory", "内存使用率", "Load", 100.0 * (memory.Total - memory.Available) / memory.Total, "%", "", now));
            rows.Add(new("system/memory/available", "Windows 内存", "Memory", "可用物理内存", "Data", memory.Available / 1073741824.0, "GiB", "", now));
        }
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback))
            {
                var stats = nic.GetIPStatistics(); double? tx = null, rx = null; long totalTx = 0, totalRx = 0;
                if (networks.TryGetValue(nic.Id, out var old))
                {
                    long dt = Math.Max(0, stats.BytesSent - old.Tx), dr = Math.Max(0, stats.BytesReceived - old.Rx);
                    double seconds = Math.Max(.001, (now - old.Time).TotalSeconds); tx = dt / seconds / 1024; rx = dr / seconds / 1024;
                    totalTx = old.TotalTx + dt; totalRx = old.TotalRx + dr;
                }
                networks[nic.Id] = (stats.BytesSent, stats.BytesReceived, now, totalTx, totalRx);
                foreach (var item in new[] { ("tx", "上传速率", tx, "KiB/s"), ("rx", "下载速率", rx, "KiB/s"), ("totalTx", "会话累计上传", (double?)(totalTx / 1048576.0), "MiB"), ("totalRx", "会话累计下载", (double?)(totalRx / 1048576.0), "MiB") })
                    rows.Add(new($"network/{nic.Id}/{item.Item1}", nic.Name, "Network", item.Item2, "Network", item.Item3, item.Item4, item.Item3.HasValue ? "" : "等待第二次采样", now));
            }
        }
        catch (Exception ex) { Error = ex.Message; }
    }
    void AddDisks(List<SensorReading> rows, DateTimeOffset now)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, PercentIdleTime, DiskReadBytesPersec, DiskWriteBytesPersec FROM Win32_PerfFormattedData_PerfDisk_PhysicalDisk WHERE Name <> '_Total'");
            searcher.Options.Timeout = TimeSpan.FromSeconds(2);
            using var disks = searcher.Get();
            foreach (ManagementObject disk in disks)
            {
                using (disk)
                {
                    string name = disk["Name"]?.ToString() ?? "未知磁盘";
                    foreach (var item in new[] { ("activity", "活动率（100 − 空闲率）", "PercentIdleTime", "%"), ("read", "读取速率", "DiskReadBytesPersec", "MiB/s"), ("write", "写入速率", "DiskWriteBytesPersec", "MiB/s") })
                    {
                        double? value = disk[item.Item3] is null ? null : Convert.ToDouble(disk[item.Item3]);
                        if (value.HasValue) value = item.Item1 == "activity" ? Math.Clamp(100 - value.Value, 0, 100) : value / 1048576;
                        rows.Add(new($"storage/performance/{name}/{item.Item1}", "磁盘 " + name, "Storage", item.Item2, "Storage", value, item.Item4, value.HasValue ? "" : "Windows 未提供性能计数器。", now));
                    }
                }
            }
            if (!rows.Any(r => r.Group == "Storage" && r.Kind == "Temperature")) rows.Add(new("unavailable/storage/temperature", "存储设备", "Storage", "磁盘温度", "Temperature", null, "°C", "当前未提供 SMART 温度；不影响磁盘活动/吞吐监控。", now));
        }
        catch (Exception ex) { rows.Add(new("unavailable/storage/performance", "存储设备", "Storage", "磁盘性能", "Storage", null, "", ex.Message, now)); }
    }
    public Metrics GetMetrics(string source, StudioSettings settings)
    {
        var rows = Snapshot; bool stale = rows.Length == 0 || DateTimeOffset.Now - rows[0].SampledAt > TimeSpan.FromSeconds(5);
        double? Pick(string key, string kind, Func<SensorReading, bool> prefer)
        {
            if (stale) return null;
            if (settings.SensorMappings.TryGetValue(source + "." + key, out var id)) return rows.FirstOrDefault(r => r.Id == id)?.Value;
            var candidates = rows.Where(r => r.Group == source && r.Kind == kind).ToArray();
            // With multiple physical devices, require a mapping rather than mixing metrics.
            if (candidates.Select(r => r.Hardware).Distinct().Count() > 1) return null;
            return candidates.FirstOrDefault(prefer)?.Value;
        }
        double? clock;
        if (settings.SensorMappings.ContainsKey(source + ".Clock")) clock = Pick("Clock", "Clock", _ => true);
        else
        {
            var clocks = rows.Where(r => r.Group == source && r.Kind == "Clock" && r.Name.Contains("Core", StringComparison.OrdinalIgnoreCase) && r.Value.HasValue).ToArray();
            clock = stale || clocks.Length == 0 || clocks.Select(r => r.Hardware).Distinct().Count() > 1 ? null : clocks.Average(r => r.Value);
        }
        return new(Pick("Temperature", "Temperature", r => r.Name.Contains(source == "CPU" ? "Package" : "Core", StringComparison.OrdinalIgnoreCase)),
            Pick("Power", "Power", r => r.Name.Contains(source == "CPU" ? "Package" : "Package", StringComparison.OrdinalIgnoreCase) || (source == "GPU" && r.Name.Contains("Board", StringComparison.OrdinalIgnoreCase))), clock,
            Pick("Load", "Load", r => r.Name.Contains(source == "CPU" ? "Total" : "Core", StringComparison.OrdinalIgnoreCase)));
    }
    public void Dispose() { stop.Cancel(); if (loop is not null && loop.Wait(TimeSpan.FromSeconds(8))) stop.Dispose(); }
}
