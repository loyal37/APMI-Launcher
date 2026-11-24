using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ApmiLauncher;

public sealed record LauncherSettings
{
    public string GameExe { get; init; } = "";
    public string OfficialLauncher { get; init; } = "";
    public string RuntimeDirectory { get; init; } = "";
    public int ProcessTimeoutSeconds { get; init; } = 180;
    public int InitializationTimeoutSeconds { get; init; } = 45;
    public bool StaticBackground { get; init; }
    public string WallpaperSource { get; init; } = "video";

    public static string DataDirectory => Path.Combine(AppContext.BaseDirectory, "data");
    public static string SettingsPath => Path.Combine(DataDirectory, "settings.json");
    public static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static LauncherSettings Read(string path)
    {
        if (!File.Exists(path)) return new LauncherSettings();
        return JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException("The settings file is empty.");
    }

    public void Save(string path) => AtomicFile.Write(path, JsonSerializer.Serialize(this, JsonOptions));

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(GameExe) || string.IsNullOrWhiteSpace(OfficialLauncher) ||
            string.IsNullOrWhiteSpace(RuntimeDirectory))
            throw new LaunchFailure("Config Check", "Please select the game, official launcher and APMI package paths in Settings.", "ValidatePaths");
        Native.RequireX64(GameExe, false);
        if (!Path.IsPathFullyQualified(OfficialLauncher) || !File.Exists(OfficialLauncher))
            throw new LaunchFailure("Config Check", "Official launcher not found. Please select launcher.exe in Settings.", "ValidateLauncher");
        Native.RequireX64(Path.Combine(RuntimeDirectory, "d3d11.dll"), true);
        Native.RequireX64(Path.Combine(RuntimeDirectory, "d3dcompiler_47.dll"), true);
        foreach (var relative in new[] { "d3dx.ini", @"Core\EFMI\main.ini" })
            if (!File.Exists(Path.Combine(RuntimeDirectory, relative)))
                throw new LaunchFailure("Config Check", "APMI package is missing required files.", relative);
        if (ProcessTimeoutSeconds is < 1 or > 600 || InitializationTimeoutSeconds is < 1 or > 120)
            throw new InvalidDataException("Invalid timeout in settings.");
    }

    public void PrepareRuntime(string sessionDirectory)
    {
        var iniPath = Path.Combine(RuntimeDirectory, "d3dx.ini");
        var original = File.ReadAllText(iniPath);
        var text = Ini.Set(original, "Loader", "target", Path.GetFullPath(GameExe));
        text = Ini.Set(text, "Loader", "loader", "APMI Launcher.exe");
        text = Ini.Set(text, "Logging", "log_level", "warning");
        text = Ini.Set(text, "Logging", "unbuffered", "1");
        text = Ini.Set(text, "Hunting", "hunting", "1");
        // XXMI 1.3.3 can spin in a one-slot region-hash cache when a tiny
        // index buffer is dumped at a second offset. Use whole-buffer hashes.
        text = Ini.Set(text, "Rendering", "track_region_hashes", "0");
        File.Copy(iniPath, Path.Combine(sessionDirectory, "d3dx-before.ini"), false);
        if (text != original) AtomicFile.Write(iniPath, text);
        Directory.CreateDirectory(Path.Combine(RuntimeDirectory, "Mods"));
        Directory.CreateDirectory(Path.Combine(RuntimeDirectory, "ShaderCache"));
        Directory.CreateDirectory(Path.Combine(RuntimeDirectory, "ShaderFixes"));
        var oldLog = Path.Combine(RuntimeDirectory, "d3d11_log.txt");
        if (File.Exists(oldLog)) File.Move(oldLog, Path.Combine(sessionDirectory, "previous-d3d11_log.txt"));
    }
}

internal static class AtomicFile
{
    internal static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var bytes = new UTF8Encoding(false).GetBytes(text);
                stream.Write(bytes);
                stream.Flush(true);
            }
            for (var attempt = 0; ; attempt++)
            {
                try { File.Move(temporary, path, true); break; }
                catch (IOException error) when ((error.HResult & 0xFFFF) is 32 or 33 && attempt < 20)
                { Thread.Sleep(15); }
            }
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

internal static class Ini
{
    internal static string Set(string text, string section, string key, string value)
    {
        if (value.Contains('\r') || value.Contains('\n')) throw new InvalidDataException("Invalid INI value.");
        var newline = text.Contains("\r\n") ? "\r\n" : "\n";
        var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
        var first = lines.FindIndex(line => line.Trim().Equals("[" + section + "]", StringComparison.OrdinalIgnoreCase));
        if (first < 0) throw new InvalidDataException("Missing INI section " + section);
        var last = lines.FindIndex(first + 1, line => Regex.IsMatch(line.Trim(), @"^\[[^\]]+\]$"));
        if (last < 0) last = lines.Count;
        var matches = Enumerable.Range(first + 1, last - first - 1)
            .Where(index => Regex.IsMatch(lines[index], @"^\s*" + Regex.Escape(key) + @"\s*=", RegexOptions.IgnoreCase)).ToArray();
        if (matches.Length > 1) throw new InvalidDataException("Duplicate INI option " + section + "." + key);
        if (matches.Length == 1) lines[matches[0]] = key + " = " + value;
        else lines.Insert(first + 1, key + " = " + value);
        return string.Join(newline, lines);
    }
}
