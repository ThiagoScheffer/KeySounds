using System.Text.Json;
using KeySounds.Core.Settings;
using KeySounds.Infrastructure.Persistence;

namespace KeySounds.Application.Diagnostics;

public sealed record LocalErrorReport(DateTimeOffset CreatedUtc, string Category, string ExceptionType, string Message, string? Operation);

public sealed class LocalErrorReportService(StoragePaths paths)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public string ReportsDirectory => Path.Combine(paths.Root, "reports");

    public async Task<string?> WriteAsync(Exception exception, ApplicationSettings settings, string category, string? operation = null, CancellationToken token = default)
    {
        if (!settings.EnableLocalCrashReports) return null;
        Directory.CreateDirectory(ReportsDirectory);
        Cleanup(settings.DiagnosticReportRetentionDays);
        var safeType = exception.GetType().Name;
        var report = new LocalErrorReport(DateTimeOffset.UtcNow, category, safeType, Sanitize(exception.Message), operation);
        var path = Path.Combine(ReportsDirectory, $"error-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss-fff}.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(report, JsonOptions), token);
        return path;
    }

    public void Cleanup(int retentionDays)
    {
        if (!Directory.Exists(ReportsDirectory)) return;
        var cutoff = DateTimeOffset.UtcNow.AddDays(-Math.Clamp(retentionDays, 1, 365));
        foreach (var file in Directory.EnumerateFiles(ReportsDirectory, "error-*.json"))
        {
            try { if (File.GetCreationTimeUtc(file) < cutoff.UtcDateTime) File.Delete(file); }
            catch { }
        }
    }

    private static string Sanitize(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return "No message.";
        var sanitized = message.Replace(Environment.UserName, "<user>", StringComparison.OrdinalIgnoreCase);
        return sanitized.Length <= 512 ? sanitized : sanitized[..512];
    }
}
