using System.Text.Json;

namespace KeySounds.Infrastructure.Persistence;

public sealed class AtomicFileWriter(JsonSerializerOptions? options = null)
{
    private readonly JsonSerializerOptions _options = options ?? new JsonSerializerOptions { WriteIndented = true };
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    public async Task WriteJsonAsync<T>(string path, T value, CancellationToken token = default)
    {
        var directory = Path.GetDirectoryName(path) ?? throw new ArgumentException("A target directory is required.", nameof(path));
        Directory.CreateDirectory(directory);
        var temporary = path + ".tmp";
        var backup = path + ".backup";
        await _writeGate.WaitAsync(token);
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, value, _options, token);
                await stream.FlushAsync(token);
                stream.Flush(true);
            }
            await using (var validation = File.OpenRead(temporary))
                _ = await JsonSerializer.DeserializeAsync<T>(validation, _options, token) ?? throw new InvalidDataException("The temporary JSON file could not be validated.");
            if (File.Exists(path)) File.Replace(temporary, path, backup, true);
            else File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); _writeGate.Release(); }
    }

    public void CleanTemporaryFiles(string directory)
    {
        if (!Directory.Exists(directory)) return;
        foreach (var file in Directory.EnumerateFiles(directory, "*.tmp", SearchOption.TopDirectoryOnly)) File.Delete(file);
    }
}
