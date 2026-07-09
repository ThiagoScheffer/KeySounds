using Avalonia;
using KeySounds.Application.Diagnostics;
using KeySounds.Application.SystemIntegration;
using KeySounds.Infrastructure.Persistence;
using System;
using System.Runtime.Versioning;

namespace KeySounds;

[SupportedOSPlatform("windows")]
class Program
{
    internal static SingleInstanceCoordinator? SingleInstance { get; private set; }

    [STAThread]
    public static void Main(string[] args)
    {
        if (TryRunDiagnostics(args)) return;
        using var singleInstance = new SingleInstanceCoordinator();
        if (!singleInstance.IsPrimary)
        {
            try { singleInstance.SignalActivationAsync().GetAwaiter().GetResult(); }
            catch { }
            return;
        }
        SingleInstance = singleInstance;
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        SingleInstance = null;
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();

    private static bool TryRunDiagnostics(string[] args)
    {
        if (args.Length < 2 || !string.Equals(args[0], "--diagnostics", StringComparison.OrdinalIgnoreCase)) return false;
        new DiagnosticReportService(new StoragePaths()).ExportAsync(args[1]).GetAwaiter().GetResult();
        return true;
    }
}
