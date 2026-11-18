using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace ASPAssistant.Core.Services;

public sealed record FeedbackReport(string Environment, string Logs, RecognitionSnapshot? Recognition)
{
    public static FeedbackReport Prepare()
    {
        var snapshot = RecognitionDiagnostics.Latest;
        var version = Assembly.GetEntryAssembly()!.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
        var details = $"程序版本：{version}\n系统：{RuntimeInformation.OSDescription}\n架构：{RuntimeInformation.ProcessArchitecture}\n运行时：{RuntimeInformation.FrameworkDescription}\n生成时间：{DateTimeOffset.Now:O}";
        var logs = "本次运行未找到可读取的日志。";
        var logPath = AppLogger.LogFilePath;
        if (logPath is not null)
        {
            try
            {
                using var input = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                const int limit = 2 * 1024 * 1024;
                var length = input.Length;
                var truncated = length > limit;
                if (truncated) input.Seek(length - limit, SeekOrigin.Begin);
                var bytes = new byte[(int)Math.Min(length, limit)];
                input.ReadExactly(bytes);
                logs = Encoding.UTF8.GetString(bytes);
                if (truncated) logs = "【日志较大，仅包含末尾约 2 MB】\n" + logs[(logs.IndexOf('\n') + 1)..];
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logs = $"无法读取日志，仍可填写问题和导出已有识别记录。\n{ex.Message}";
            }
        }
        return new(Sanitize(details), Sanitize(logs), snapshot is null ? null : snapshot with { Result = Sanitize(snapshot.Result) });
    }

    public string Preview(string category, string description, string steps) =>
        $"问题类型：{category}\n问题描述：\n{Sanitize(description)}\n\n复现步骤：\n{Sanitize(steps)}\n\n{Environment}\n\n识别记录：\n" +
        (Recognition is { } frame ? $"时间：{frame.CapturedAt:O}\n来源：{frame.Source}\n{frame.Result}" : "暂无识别记录。") +
        $"\n\n近期日志：\n{Logs}";

    public void Export(string path, string category, string description, string steps, bool includeScreenshot)
    {
        using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        using var zip = new ZipArchive(output, ZipArchiveMode.Create);
        using (var writer = new StreamWriter(zip.CreateEntry("反馈说明.txt").Open(), Encoding.UTF8))
            writer.Write(Preview(category, description, steps));
        if (includeScreenshot && Recognition is { Frame.Length: > 0 } snapshot)
        {
            using var image = zip.CreateEntry("识别画面.png", CompressionLevel.NoCompression).Open();
            image.Write(snapshot.Frame);
        }
    }

    public static string Sanitize(string text)
    {
        text = Regex.Replace(text, @"(?im)^(.*\b(?:authorization|cookie|set-cookie|password|passwd|secret|api[_-]?key|access[_-]?token|refresh[_-]?token)\b\s*[:=]).*$", "$1 <已隐藏>");
        text = Regex.Replace(text, @"(?i)(https?://)[^\s/@]+:[^\s/@]+@", "$1<已隐藏>@");
        text = Regex.Replace(text, @"(?i)(https?://[^\s?""<>]+)\?[^\s""<>]*", "$1?<参数已隐藏>");
        text = Regex.Replace(text, @"(?i)(?<![a-z0-9])(?:[a-z]:[\\/]|\\\\)[^\r\n""<>|]*", "<本地路径>");
        return text;
    }
}
