using Avalonia;
using KeySounds.Application.Diagnostics;
using KeySounds.Infrastructure.Persistence;
using System;

namespace KeySounds;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        if (TryRunDiagnostics(args)) return;
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
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
