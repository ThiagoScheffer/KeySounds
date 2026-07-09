using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;

namespace KeySounds.Application.SystemIntegration;

public sealed class SingleInstanceCoordinator : IDisposable
{
    private readonly string _pipeName;
    private readonly Mutex _mutex;
    private readonly CancellationTokenSource _lifetime = new();
    private Task? _serverTask;
    private bool _ownsMutex;
    private bool _disposed;

    public SingleInstanceCoordinator(string? discriminator = null)
    {
        var user = Environment.UserName;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(discriminator ?? AppContext.BaseDirectory)))[..16];
        var name = $"KeySounds.{user}.{hash}";
        _pipeName = name + ".activate";
        _mutex = new Mutex(true, @"Local\" + name, out _ownsMutex);
    }

    public bool IsPrimary => _ownsMutex;
    public event EventHandler? ActivationRequested;

    public void StartActivationServer()
    {
        if (!_ownsMutex || _serverTask is not null) return;
        _serverTask = Task.Run(() => ServerLoopAsync(_lifetime.Token));
    }

    public async Task SignalActivationAsync(CancellationToken token = default)
        => await SignalAsync("activate", token);

    public async Task SignalAsync(string command, CancellationToken token = default)
    {
        await using var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(500, token);
        var bytes = Encoding.UTF8.GetBytes(command);
        await pipe.WriteAsync(bytes, token);
        await pipe.FlushAsync(token);
    }

    private async Task ServerLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            await using var server = new NamedPipeServerStream(_pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            try
            {
                await server.WaitForConnectionAsync(token);
                var buffer = new byte[32];
                var read = await server.ReadAsync(buffer, token);
                var command = Encoding.UTF8.GetString(buffer, 0, read);
                if (string.Equals(command, "activate", StringComparison.Ordinal)) ActivationRequested?.Invoke(this, EventArgs.Empty);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch { }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        try { _serverTask?.Wait(TimeSpan.FromMilliseconds(500)); } catch { }
        if (_ownsMutex) { try { _mutex.ReleaseMutex(); } catch { } _ownsMutex = false; }
        _mutex.Dispose();
        _lifetime.Dispose();
    }
}
