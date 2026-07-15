using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace KeySounds.Application.SoundPacks;

public interface ISoundPackFolderPicker
{
    Task<string?> PickImportFolderAsync(CancellationToken token = default);
    Task<IReadOnlyList<string>> PickSampleFilesAsync(CancellationToken token = default);
}

public sealed class AvaloniaSoundPackFolderPicker(Func<TopLevel?> owner) : ISoundPackFolderPicker
{
    private static readonly FilePickerFileType Wav = new("WAV audio") { Patterns = ["*.wav"], MimeTypes = ["audio/wav", "audio/x-wav"] };

    public async Task<string?> PickImportFolderAsync(CancellationToken token = default)
    {
        var topLevel = owner();
        if (topLevel?.StorageProvider is null) return null;
        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            AllowMultiple = false,
            Title = "Import sound pack folder"
        });
        token.ThrowIfCancellationRequested();
        return folders.Count == 0 ? null : folders[0].TryGetLocalPath();
    }

    public async Task<IReadOnlyList<string>> PickSampleFilesAsync(CancellationToken token = default)
    {
        var topLevel = owner();
        if (topLevel?.StorageProvider is null) return [];
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = true,
            FileTypeFilter = [Wav],
            Title = "Add WAV samples"
        });
        token.ThrowIfCancellationRequested();
        return files.Select(file => file.TryGetLocalPath()).Where(path => !string.IsNullOrWhiteSpace(path)).Select(path => path!).ToArray();
    }
}
