using System.Collections.Concurrent;
using KeySounds.Core.Profiles;

namespace KeySounds.Application.Routing;

public sealed class SoundVariationSelector
{
    private readonly ConcurrentDictionary<string, int> _state = new();
    public string Select(string key, IReadOnlyList<string> files, VariationSelectionMode mode)
    {
        if (files.Count == 0) throw new InvalidOperationException("The sound set contains no samples.");
        if (files.Count == 1) return files[0];
        var next = mode switch
        {
            VariationSelectionMode.Random => Random.Shared.Next(files.Count),
            VariationSelectionMode.RoundRobin => _state.AddOrUpdate(key, 0, (_, previous) => (previous + 1) % files.Count),
            _ => _state.AddOrUpdate(key, Random.Shared.Next(files.Count), (_, previous) => (previous + 1 + Random.Shared.Next(files.Count - 1)) % files.Count)
        };
        return files[next % files.Count];
    }
    public void Clear() => _state.Clear();
}
