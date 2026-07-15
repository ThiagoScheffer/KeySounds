using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using KeySounds.ViewModels;

namespace KeySounds;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private async void SoundPackDraftSetDrop(object? sender, DragEventArgs e)
    {
        if (sender is not Control { DataContext: SoundPackDraftSetViewModel set }) return;
        var files = e.DataTransfer.Items
            .SelectMany(item => item.Formats.Select(format => item.TryGetRaw(format)))
            .SelectMany(raw => raw switch
            {
                IStorageItem storage => [storage.TryGetLocalPath()],
                IEnumerable<IStorageItem> storageItems => storageItems.Select(item => item.TryGetLocalPath()),
                _ => []
            })
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (files.Length == 0) return;
        await set.AddDroppedFilesAsync(files);
        e.Handled = true;
    }
}
