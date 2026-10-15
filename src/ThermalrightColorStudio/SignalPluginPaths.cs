using System.IO;

namespace ThermalrightColorStudio;

public static class SignalPluginPaths
{
    public const string ScriptName = "ColorStudio_SignalRGB_Bridge.js";

    public static string? FindProgramDirectory(string appRoot, string? activeSignalDirectory)
    {
        if (!Directory.Exists(appRoot)) return null;
        var candidates = Directory.EnumerateDirectories(appRoot, "app-*")
            .Select(path => new { Directory = Path.Combine(path, "Signal-x64"), Version = Version.TryParse(Path.GetFileName(path)[4..], out var version) ? version : null })
            .Where(item => item.Version is not null && File.Exists(Path.Combine(item.Directory, "SignalRgb.exe")))
            .OrderByDescending(item => item.Version).ToArray();
        var selected = candidates.FirstOrDefault(item => activeSignalDirectory is not null && string.Equals(Path.GetFullPath(item.Directory), Path.GetFullPath(activeSignalDirectory), StringComparison.OrdinalIgnoreCase))
            ?? candidates.FirstOrDefault();
        return selected is null ? null : Path.Combine(selected.Directory, "Plugins", "ColorStudio");
    }

    public static bool CanRepair(string appRoot, string directory)
    {
        // Explicitly disabled files must stay disabled, including crash-protection rollback.
        if (File.Exists(Path.Combine(directory, ScriptName + ".disabled")) || File.Exists(Path.Combine(directory, "ColorStudio_SignalRGB_Bridge.qml.disabled"))) return false;
        if (!Directory.Exists(appRoot)) return false;
        foreach (var version in Directory.EnumerateDirectories(appRoot, "app-*"))
        {
            string path = Path.Combine(version, "Signal-x64", "Plugins", "ColorStudio", ScriptName);
            if (!File.Exists(path)) continue;
            using var reader = new StreamReader(path);
            if (reader.ReadLine()?.StartsWith("// Color Studio SignalRGB bridge.", StringComparison.Ordinal) == true) return true;
        }
        // Never silently perform a first installation.
        return false;
    }
}
