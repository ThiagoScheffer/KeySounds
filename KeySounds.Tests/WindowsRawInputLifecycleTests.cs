using KeySounds.Platform.Windows.Input;
using Xunit;

namespace KeySounds.Tests;

public sealed class WindowsRawInputLifecycleTests
{
    [Fact]
    public async Task ImmediateStopCannotLeaveStartupRunning()
    {
        if (!OperatingSystem.IsWindows()) return;
        await using var input = new WindowsRawInputSource();
        var start = input.StartAsync();
        var stop = input.StopAsync();
        try { await start; } catch (OperationCanceledException) { } catch (InvalidOperationException) { }
        await stop;
    }

    [Fact]
    public async Task ConcurrentStopCallersAwaitNativeThreadExit()
    {
        if (!OperatingSystem.IsWindows()) return;
        await using var input = new WindowsRawInputSource();
        await input.StartAsync();
        await Task.WhenAll(input.StopAsync(), input.StopAsync());
    }

    [Fact]
    public async Task RepeatedImmediateStartupShutdownCompletes()
    {
        if (!OperatingSystem.IsWindows()) return;
        for (var index = 0; index < 10; index++)
        {
            await using var input = new WindowsRawInputSource();
            var start = input.StartAsync();
            var stop = input.StopAsync();
            try { await start; } catch (OperationCanceledException) { } catch (InvalidOperationException) { }
            await stop;
        }
    }
}
