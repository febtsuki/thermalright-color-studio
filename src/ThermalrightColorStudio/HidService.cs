using HidSharp;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.ComponentModel;
using Microsoft.Win32.SafeHandles;

namespace ThermalrightColorStudio;

public record HidInfo(string Path, string Product, int Vid, int Pid, int InputLength, int OutputLength, int FeatureLength, string ReportDescriptor, string Error);
public sealed class HidService : IDisposable
{
    HidStream? stream;
    SafeFileHandle? writer;
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool WriteFile(SafeFileHandle file, byte[] buffer, uint length, out uint written, IntPtr overlapped);
    void WriteReport(byte[] report)
    {
        if (writer is null || writer.IsInvalid || !WriteFile(writer, report, (uint)report.Length, out uint count, IntPtr.Zero)) throw new IOException("同步 HID 写入失败：" + new Win32Exception(Marshal.GetLastWin32Error()).Message);
        if (count != report.Length) throw new IOException($"HID 报告写入不完整：{count}/{report.Length}");
    }
    public int? Pm { get; private set; }
    public int? Sub { get; private set; }
    public bool Connected => stream is not null;
    public bool Supported => Pm is 48 or 49; // LF8 wire mapping only. No speculative fallback.
    public string Status { get; private set; } = "未连接 · 尚未发送设备命令";
    public string RawHandshake { get; private set; } = "";
    public string DevicePath { get; private set; } = "";
    DateTime lastTrccCheck;
    record IdentityCache(string Path, string Descriptor, string Reply, long BootTime);
    public static bool TrccRunning => Process.GetProcesses().Any(p => { using (p) return p.ProcessName.Equals("TRCC", StringComparison.OrdinalIgnoreCase); });
    public static HidInfo[] Enumerate()
    {
        var list = new List<HidInfo>();
        foreach (var d in DeviceList.Local.GetHidDevices())
        {
            try { list.Add(new(d.DevicePath, d.GetProductName(), d.VendorID, d.ProductID, d.GetMaxInputReportLength(), d.GetMaxOutputReportLength(), d.GetMaxFeatureReportLength(), Convert.ToHexString(d.GetRawReportDescriptor()), "")); }
            catch (Exception ex) { list.Add(new(d.DevicePath, "读取信息失败", d.VendorID, d.ProductID, 0, 0, 0, "", ex.Message)); }
        }
        return list.ToArray();
    }
    public void Connect(string? path = null)
    {
        Dispose(); Pm = Sub = null; RawHandshake = "";
        if (TrccRunning) throw new IOException("请先完全退出 TRCC（包括托盘），再连接设备。");
        var devices = DeviceList.Local.GetHidDevices(0x0416, 0x8001).Where(d => path is null || d.DevicePath == path).ToArray();
        if (devices.Length != 1) throw new IOException(devices.Length == 0 ? "未找到候选设备 0416:8001。" : "发现多个候选设备，请在诊断页选择设备路径。");
        var device = devices[0]; DevicePath = device.DevicePath;
        if (device.GetMaxOutputReportLength() != 65 || device.GetMaxInputReportLength() != 65) throw new IOException("报告长度与已实现的 64 字节 + Report ID 协议不符，已停止。");
        try
        {
            stream = device.Open(); stream.ReadTimeout = 1800; stream.WriteTimeout = 1000;
            // Match the installed TRCC: separate synchronous write handle, overlapped reader.
            writer = CreateFile(DevicePath, 0x40000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
            if (writer.IsInvalid) throw new IOException(new Win32Exception(Marshal.GetLastWin32Error()).Message);
            Thread.Sleep(50); var init = new byte[65]; init[1] = 0xDA; init[2] = 0xDB; init[3] = 0xDC; init[4] = 0xDD; init[13] = 1;
            WriteReport(init); Thread.Sleep(200);
            var reply = new byte[65]; int count;
            bool cached = false;
            string descriptor = Convert.ToHexString(device.GetRawReportDescriptor());
            long boot = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - Environment.TickCount64;
            string cachePath = Path.Combine(SettingsStore.Root, "device-identity.json");
            try { count = stream.Read(reply); }
            catch (TimeoutException)
            {
                // Only this boot, exact path and descriptor. Never fall back to VID/PID alone.
                if (!File.Exists(cachePath)) throw;
                var cache = System.Text.Json.JsonSerializer.Deserialize<IdentityCache>(File.ReadAllText(cachePath), SettingsStore.Json);
                if (cache is null || cache.Path != DevicePath || cache.Descriptor != descriptor || Math.Abs(cache.BootTime - boot) > 10000) throw;
                reply = Convert.FromHexString(cache.Reply); count = reply.Length; cached = true;
            }
            RawHandshake = Convert.ToHexString(reply.AsSpan(0, count));
            if (count != 65 || reply[0] != 0 || reply[1] != 0xDA || reply[2] != 0xDB || reply[3] != 0xDC || reply[4] != 0xDD || reply[13] != 1) throw new IOException("握手响应未通过头部/命令验证，已保留原始数据；没有发送颜色。");
            // HidSharp includes Report ID at [0]. Current upstream extracts raw payload PM[5], SUB[4].
            Pm = reply[6]; Sub = reply[5];
            Status = Supported ? $"{(cached ? "本次开机已验证身份缓存" : "已握手")} · PM={Pm} SUB={Sub} · LF8 / 93 段候选" : $"未知或尚未支持布局 · PM={Pm} SUB={Sub} · 禁止颜色写入";
            if (Supported && !cached) { Directory.CreateDirectory(SettingsStore.Root); File.WriteAllText(cachePath, System.Text.Json.JsonSerializer.Serialize(new IdentityCache(DevicePath, descriptor, RawHandshake, boot), SettingsStore.Json)); }
            if (!Supported) { stream.Dispose(); stream = null; writer.Dispose(); writer = null; }
        }
        catch { stream?.Dispose(); stream = null; writer?.Dispose(); writer = null; Status = "连接失败 · 请查看诊断"; throw; }
    }
    public static byte[][] Encode(Rgb[] logical)
    {
        if (logical.Length != 93) throw new ArgumentException("LF8 必须包含 93 个逻辑段。");
        var packet = new byte[20 + 93 * 3]; packet[0] = 0xDA; packet[1] = 0xDB; packet[2] = 0xDC; packet[3] = 0xDD; packet[12] = 2;
        packet[16] = (byte)(279 & 255); packet[17] = (byte)(279 >> 8);
        for (int i = 0; i < 93; i++) { var c = logical[PanelLayout.WireMap[i]]; packet[20 + i * 3] = (byte)(c.R * .4); packet[21 + i * 3] = (byte)(c.G * .4); packet[22 + i * 3] = (byte)(c.B * .4); }
        var reports = new List<byte[]>();
        for (int offset = 0; offset < packet.Length; offset += 64) { var report = new byte[65]; Array.Copy(packet, offset, report, 1, Math.Min(64, packet.Length - offset)); reports.Add(report); }
        return reports.ToArray();
    }
    public void Send(Rgb[] colors)
    {
        if (stream is null || !Supported) throw new IOException("未连接受支持的布局。");
        if (DateTime.UtcNow - lastTrccCheck > TimeSpan.FromSeconds(1)) { lastTrccCheck = DateTime.UtcNow; if (TrccRunning) { Dispose(); throw new IOException("检测到 TRCC 启动，已停止本软件写入。"); } }
        foreach (var report in Encode(colors)) WriteReport(report);
        Thread.Sleep(30); // Original TRCC's complete-frame cooldown; keep handle alive until settled.
    }
    public void Dispose() { stream?.Dispose(); stream = null; writer?.Dispose(); writer = null; Status = "已断开 · 停止发送"; }
}
