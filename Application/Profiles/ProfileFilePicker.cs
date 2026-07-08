using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace KeySounds.Application.Profiles;

public interface IProfileFilePicker
{
    Task<string?> PickImportAsync(CancellationToken token = default);
    Task<string?> PickExportAsync(string suggestedName, CancellationToken token = default);
}

public sealed class AvaloniaProfileFilePicker(Func<TopLevel?> owner) : IProfileFilePicker
{
    private static readonly FilePickerFileType Json = new("JSON profile") { Patterns = ["*.json"], MimeTypes = ["application/json"] };
    public async Task<string?> PickImportAsync(CancellationToken token = default)
    {
        var topLevel = owner(); if (topLevel?.StorageProvider is null) return null;
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new() { AllowMultiple = false, FileTypeFilter = [Json], Title = "Import profile" });
        return files.Count == 0 ? null : files[0].TryGetLocalPath();
    }
    public async Task<string?> PickExportAsync(string suggestedName, CancellationToken token = default)
    {
        var topLevel = owner(); if (topLevel?.StorageProvider is null) return null;
        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new() { SuggestedFileName = suggestedName + ".json", DefaultExtension = "json", FileTypeChoices = [Json], Title = "Export profile" });
        return file?.TryGetLocalPath();
    }
}
