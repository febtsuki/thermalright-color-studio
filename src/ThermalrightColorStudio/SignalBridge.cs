using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;

namespace ThermalrightColorStudio;
public sealed record SignalFrame(Rgb[] Colors, long Timestamp, uint Sequence);
public sealed class SignalBridge : IDisposable
{
    public const int InfoPort = 18742, ColorPort = 18743, PacketLength = 304;
    readonly byte[] token = RandomNumberGenerator.GetBytes(16);
    readonly CancellationTokenSource stop = new();
    HttpListener? http; UdpClient? udp; Task? httpTask, udpTask;
    SignalFrame? latest; long received, rejected; string error = ""; bool disposed;
    public string Error => Volatile.Read(ref error);
    public long Received => Interlocked.Read(ref received);
    public long Rejected => Interlocked.Read(ref rejected);
    public SignalFrame? Latest => Volatile.Read(ref latest);
    public bool Running => http is not null && udp is not null;
    public bool Fresh => Error.Length == 0 && Latest is { } frame && Stopwatch.GetElapsedTime(frame.Timestamp).TotalSeconds < 2;
    public string Status => !Running ? "同步未启用" : Error.Length > 0 ? Error : Fresh ? $"SignalRGB 已同步 · {Received} 帧" : Received == 0 ? "等待 SignalRGB · 请安装插件并重启 SignalRGB" : "SignalRGB 数据已中断";
    public void Start()
    {
        if (Running) return;
        try
        {
            udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, ColorPort));
            http = new HttpListener(); http.Prefixes.Add($"http://127.0.0.1:{InfoPort}/"); http.Start();
            udpTask = Task.Run(Receive); httpTask = Task.Run(Serve);
        }
        catch { Dispose(); throw; }
    }
    async Task Serve()
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                var context = await http!.GetContextAsync().WaitAsync(stop.Token);
                try
                {
                    bool statusRequest = context.Request.Url?.AbsolutePath == "/status";
                    bool valid = context.Request.HttpMethod == "GET" && (context.Request.Url?.AbsolutePath == "/info" || statusRequest) && context.Request.RemoteEndPoint is { } peer && IPAddress.IsLoopback(peer.Address);
                    context.Response.StatusCode = valid ? 200 : 404;
                    context.Response.ContentType = "application/json"; context.Response.Headers["Cache-Control"] = "no-store";
                    var bytes = Encoding.UTF8.GetBytes(valid ? statusRequest ? JsonSerializer.Serialize(new { received = Received, rejected = Rejected, fresh = Fresh, status = Status }) : JsonSerializer.Serialize(new { name = "Color Studio · PS120 Digital", version = 1, ledCount = 93, udpPort = ColorPort, token = Convert.ToHexString(token) }) : "{}");
                    context.Response.ContentLength64 = bytes.Length; await context.Response.OutputStream.WriteAsync(bytes, stop.Token);
                }
                catch (Exception ex) when (ex is IOException or HttpListenerException or OperationCanceledException) { }
                finally { context.Response.Close(); }
            }
        }
        catch (Exception ex) { if (!stop.IsCancellationRequested) Volatile.Write(ref error, "同步发现服务停止：" + ex.Message); }
    }
    async Task Receive()
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                var packet = await udp!.ReceiveAsync(stop.Token);
                if (!IPAddress.IsLoopback(packet.RemoteEndPoint.Address) || !Decode(packet.Buffer, token, out var colors, out var sequence)) { Interlocked.Increment(ref rejected); continue; }
                var previous = Latest;
                // Permit a restarted plugin's sequence only after its stream has expired.
                if (Fresh && previous is not null && unchecked((int)(sequence - previous.Sequence)) <= 0) { Interlocked.Increment(ref rejected); continue; }
                Volatile.Write(ref latest, new(colors, Stopwatch.GetTimestamp(), sequence)); Interlocked.Increment(ref received);
            }
        }
        catch (Exception ex) { if (!stop.IsCancellationRequested) Volatile.Write(ref error, "同步接收停止：" + ex.Message); }
    }
    public static bool Decode(byte[] packet, byte[] expectedToken, out Rgb[] colors, out uint sequence)
    {
        colors = []; sequence = 0;
        if (packet.Length != PacketLength || expectedToken.Length != 16 || packet[0] != 84 || packet[1] != 67 || packet[2] != 83 || packet[3] != 82 || packet[4] != 1 || !CryptographicOperations.FixedTimeEquals(packet.AsSpan(5, 16), expectedToken)) return false;
        sequence = BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(21, 4)); colors = new Rgb[93];
        for (int i = 0; i < 93; i++) colors[i] = new(packet[25 + i * 3], packet[26 + i * 3], packet[27 + i * 3]); return true;
    }
    public static Rgb[] MaskFrame(Rgb[] colors, StudioSettings settings, Metrics metrics, string source)
    {
        if (colors.Length != 93) throw new ArgumentException("同步颜色需要 93 个逻辑段。");
        var mask = PanelLayout.Mask(metrics, source, settings.Fahrenheit); var output = new Rgb[93];
        if (!settings.Enabled) return output;
        colors = SignalColorSmoothing.Apply(colors, settings.SignalRgbSmoothing);
        for (int i = 0; i < 93; i++) if (mask[i]) output[i] = colors[i].Scale(settings.Brightness / 100); return output;
    }
    public static string InstallPlugin()
    {
        string? userDirectory = Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\WhirlwindFX\SignalRgb", "UserDirectory", null) as string;
        string directory = Path.Combine(string.IsNullOrWhiteSpace(userDirectory) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "WhirlwindFX") : userDirectory, "Plugins");
        // Prefer an existing Color Studio installation under SignalRGB's ASCII program path.
        // This avoids reintroducing the user's non-ASCII OneDrive path after a migration.
        string appRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VortxEngine");
        if (Directory.Exists(appRoot))
        {
            var existing = Directory.EnumerateDirectories(appRoot, "app-*").Select(path => new { Directory = Path.Combine(path, "Signal-x64", "Plugins", "ColorStudio"), Version = System.Version.TryParse(Path.GetFileName(path)[4..], out var version) ? version : new System.Version() })
                .Where(item => File.Exists(Path.Combine(item.Directory, "ColorStudio_SignalRGB_Bridge.js")))
                .OrderByDescending(item => item.Version).FirstOrDefault();
            if (existing is not null) directory = existing.Directory;
        }
        Directory.CreateDirectory(directory);
        var assembly = System.Reflection.Assembly.GetExecutingAssembly();
        // Install the service page before the script so a fresh scan sees a complete pair.
        foreach (string extension in new[] { "qml", "js" })
        {
            string path = Path.Combine(directory, "ColorStudio_SignalRGB_Bridge." + extension);
            using var stream = assembly.GetManifestResourceStream("ThermalrightColorStudio.SignalRGBBridge." + extension) ?? throw new IOException("桥接插件资源缺失。");
            using var reader = new StreamReader(stream); string source = reader.ReadToEnd();
            if (File.Exists(path) && File.ReadAllText(path) != source) File.Copy(path, path + ".backup-" + DateTime.Now.ToString("yyyyMMddHHmmss"), true);
            string temporary = path + ".installing"; File.WriteAllText(temporary, source, new UTF8Encoding(false)); File.Move(temporary, path, true);
        }
        return Path.Combine(directory, "ColorStudio_SignalRGB_Bridge.js");
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        stop.Cancel(); http?.Close(); udp?.Dispose(); http = null; udp = null;
        try { Task.WhenAll(new[] { httpTask, udpTask }.Where(t => t is not null).Cast<Task>()).Wait(1500); } catch { }
        stop.Dispose();
    }
}
