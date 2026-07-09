using Avalonia.Controls;
using Avalonia.Layout;
using KeySounds.Core.SoundPacks;

namespace KeySounds.Application.SoundPacks;

public interface ISoundPackConflictResolver
{
    Task<SoundPackConflictPolicy> ResolveDuplicateAsync(InstalledSoundPack existing, CancellationToken token = default);
}

public sealed class CancelSoundPackConflictResolver : ISoundPackConflictResolver
{
    public Task<SoundPackConflictPolicy> ResolveDuplicateAsync(InstalledSoundPack existing, CancellationToken token = default) => Task.FromResult(SoundPackConflictPolicy.Cancel);
}

public sealed class AvaloniaSoundPackConflictResolver(Func<Window?> owner) : ISoundPackConflictResolver
{
    public async Task<SoundPackConflictPolicy> ResolveDuplicateAsync(InstalledSoundPack existing, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (existing.IsBuiltIn) return SoundPackConflictPolicy.Cancel;
        var parent = owner();
        if (parent is null) return SoundPackConflictPolicy.Cancel;
        var dialog = new Window
        {
            Width = 460,
            Height = 220,
            CanResize = false,
            Title = "Replace sound pack?",
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        var result = SoundPackConflictPolicy.Cancel;
        var cancel = new Button { Content = "Cancel", MinWidth = 90 };
        var replace = new Button { Content = "Replace", MinWidth = 90 };
        cancel.Click += (_, _) => { result = SoundPackConflictPolicy.Cancel; dialog.Close(); };
        replace.Click += (_, _) => { result = SoundPackConflictPolicy.Replace; dialog.Close(); };
        dialog.Content = new StackPanel
        {
            Margin = new(18),
            Spacing = 14,
            Children =
            {
                new TextBlock { FontSize = 20, FontWeight = Avalonia.Media.FontWeight.SemiBold, Text = "Sound pack already installed" },
                new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap, Text = $"'{existing.Manifest?.Name ?? existing.Manifest?.Id ?? "This pack"}' is already installed. Replacing it will overwrite the managed copy in KeySounds storage. The source folder will not be modified." },
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { cancel, replace } }
            }
        };
        await dialog.ShowDialog(parent);
        token.ThrowIfCancellationRequested();
        return result;
    }
}
