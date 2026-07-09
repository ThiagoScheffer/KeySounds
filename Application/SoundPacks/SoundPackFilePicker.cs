using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace KeySounds.Application.SoundPacks;

public interface ISoundPackFolderPicker
{
    Task<string?> PickImportFolderAsync(CancellationToken token = default);
}

public sealed class AvaloniaSoundPackFolderPicker(Func<TopLevel?> owner) : ISoundPackFolderPicker
{
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
}
