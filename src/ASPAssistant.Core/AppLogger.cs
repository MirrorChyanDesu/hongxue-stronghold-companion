using System.Reflection;

namespace ASPAssistant.Core;

/// <summary>
/// Lightweight file logger. Initialize once at startup with <see cref="Initialize"/>;
/// all subsequent calls to Info/Warn/Error are thread-safe.
/// Log files are written to the supplied directory as <c>aspa-yyyy-MM-dd.log</c>.
/// Files older than 7 days are deleted automatically on startup.
/// </summary>
public static class AppLogger
{
    private static string? _logFilePath;
    private static readonly object _lock = new();
    private static readonly Dictionary<string, (long At, int Suppressed)> _repeated = new();
    private static Func<bool> _isGamePresent = static () => true;
    public static string? LogFilePath => _logFilePath;

    public static void SetGamePresenceCheck(Func<bool> isGamePresent) =>
        Volatile.Write(ref _isGamePresent, isGamePresent);

    /// <summary>
    /// Call once at application startup. Creates the log directory, prunes old logs,
    /// and writes the first entry with version and runtime information.
    /// </summary>
    public static void Initialize(string logDir)
    {
        try
        {
            Directory.CreateDirectory(logDir);

            // Prune log files older than 7 days.
            foreach (var old in Directory.GetFiles(logDir, "aspa-*.log"))
            {
                if (File.GetLastWriteTime(old) < DateTime.Now.AddDays(-7))
                    try { File.Delete(old); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        System.Diagnostics.Debug.WriteLine($"[AppLogger] Cannot prune {old}: {ex.Message}");
                    }
            }

            var date = DateTime.Now.ToString("yyyy-MM-dd");
            _logFilePath = Path.Combine(logDir, $"aspa-{date}.log");

            var version = Assembly.GetEntryAssembly()
                ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion ?? "unknown";
            var runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;

            WriteLine("INFO", "AppLogger", $"===== ASPAssistant {version} | {runtime} | {DateTime.Now:yyyy-MM-dd HH:mm:ss} =====");
            PruneArtifacts(Path.Combine(logDir, "diagnostics"), null);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AppLogger] Failed to initialise: {ex.Message}");
        }
    }

    public static void Info(string component, string message) =>
        WriteLine("INFO ", component, message);

    public static void Warn(string component, string message) =>
        WriteLine("WARN ", component, message);

    public static void Error(string component, string message) =>
        WriteLine("ERROR", component, message);

    public static void Error(string component, string message, Exception ex) =>
        WriteLine("ERROR", component, $"{message}: {ex}");

    public static void RecognitionFailure(string component, string stage, string detail)
    {
        lock (_lock)
        {
            var key = component + ":" + stage;
            var now = System.Diagnostics.Stopwatch.GetTimestamp();
            _repeated.TryGetValue(key, out var previous);
            if (previous.At != 0 && System.Diagnostics.Stopwatch.GetElapsedTime(previous.At, now) < TimeSpan.FromSeconds(30))
            {
                _repeated[key] = (previous.At, previous.Suppressed + 1);
                return;
            }
            _repeated[key] = (now, 0);
            WriteLine("ERROR", component, $"{stage}: {detail}; suppressed={previous.Suppressed}");
        }
    }

    public static string? SaveArtifact(string prefix, byte[] bytes, string extension = ".bin")
    {
        try
        {
            if (!Volatile.Read(ref _isGamePresent)() || _logFilePath is null) return null;
            lock (_lock)
            {
                var dir = Path.Combine(Path.GetDirectoryName(_logFilePath)!, "diagnostics");
                Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, $"aspa-artifact-{DateTime.Now:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}-{prefix}{extension}");
                File.WriteAllBytes(path, bytes);
                PruneArtifacts(dir, path);
                return path;
            }
        }
        catch (Exception ex)
        {
            WriteLine("WARN ", "AppLogger", $"Failed to save artifact: {ex.Message}");
            return null;
        }
    }

    // Keep at most 50 generated captures for seven days; legacy evidence beside the log is untouched.
    private static void PruneArtifacts(string directory, string? latest)
    {
        var root = new DirectoryInfo(Path.GetFullPath(directory));
        FileInfo[] files;
        try { files = root.GetFiles("aspa-artifact-*"); }
        catch (DirectoryNotFoundException) { return; }
        if ((root.Attributes & FileAttributes.ReparsePoint) != 0) return;
        var cutoff = DateTime.UtcNow.AddDays(-7);
        var ordered = files.OrderByDescending(file => file.FullName == latest)
            .ThenByDescending(file => file.LastWriteTimeUtc).ThenByDescending(file => file.Name);
        var kept = 0;
        foreach (var file in ordered)
        {
            if ((file.Attributes & FileAttributes.ReparsePoint) != 0) continue;
            if (file.LastWriteTimeUtc >= cutoff && kept++ < 50) continue;
            try { file.Delete(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                System.Diagnostics.Debug.WriteLine($"[AppLogger] Cannot prune {file.Name}: {ex.Message}");
            }
        }
    }

    private static void WriteLine(string level, string component, string message)
    {
        if ((level == "INFO " || (level == "WARN " && component is "ScreenCapture" or "AdbCapture"))
            && !Volatile.Read(ref _isGamePresent)()) return;
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] [{level}] [{component}] {message}";
        System.Diagnostics.Debug.WriteLine(line);

        if (_logFilePath == null) return;
        lock (_lock)
        {
            try { File.AppendAllText(_logFilePath, line + Environment.NewLine); }
            catch { /* Never let logging crash the app */ }
        }
    }
}
