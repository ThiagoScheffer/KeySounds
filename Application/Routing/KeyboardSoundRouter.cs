using KeySounds.Application.Profiles;
using KeySounds.Core.Audio;
using KeySounds.Core.Input;

namespace KeySounds.Application.Routing;

public interface ISoundSetCatalog
{
    bool TryGetFiles(string packId, string setId, out IReadOnlyList<string> files);
}

public sealed class BuiltInSoundSetCatalog : ISoundSetCatalog
{
    private static readonly IReadOnlyList<string> Click = ["key-click.wav"];
    public bool TryGetFiles(string packId, string setId, out IReadOnlyList<string> files)
    {
        files = Click;
        return packId == "builtin" && setId == "click";
    }
}

public sealed class KeyboardSoundRouter(ActiveProfileService profiles, KeyGroupResolver groups, SoundVariationSelector variations, ISoundSetCatalog catalog)
{
    private readonly Dictionary<(nint Device, KeyIdentifier Key), long> _pressed = [];
    public float GlobalVolume { get; set; } = 1f;

    public SoundTrigger? Route(KeyboardInputEvent input)
    {
        var profile = profiles.Current;
        var key = new KeyIdentifier(input.ScanCode, input.IsExtended);
        var stateKey = (input.DeviceHandle, key);
        var isDown = input.Action == KeyboardAction.KeyDown;
        long lastTrigger = 0;
        var isRepeat = isDown && _pressed.TryGetValue(stateKey, out lastTrigger);

        if (!isDown) _pressed.Remove(stateKey);
        else if (!isRepeat) _pressed[stateKey] = input.Timestamp;
        else
        {
            if (profile.RepeatMode == Core.Profiles.RepeatSoundMode.Suppress) return null;
            if (profile.RepeatMode == Core.Profiles.RepeatSoundMode.Throttled)
            {
                var elapsed = (input.Timestamp - lastTrigger) * 1000d / System.Diagnostics.Stopwatch.Frequency;
                if (elapsed < profile.RepeatMinimumIntervalMilliseconds) return null;
            }
            _pressed[stateKey] = input.Timestamp;
        }

        if (!profile.Enabled) return null;
        if ((isDown && !profile.PlayOnKeyDown) || (!isDown && !profile.PlayOnKeyUp)) return null;
        if (!profile.Groups.TryGetValue(groups.Resolve(key), out var mapping) || !mapping.Enabled) return null;
        profile.Overrides.TryGetValue(key, out var keyOverride);
        if (keyOverride is { Enabled: false }) return null;
        var pack = keyOverride?.PackId ?? mapping.PackId;
        var set = isDown ? keyOverride?.DownSetId ?? mapping.DownSetId : keyOverride?.UpSetId ?? mapping.UpSetId;
        if (string.IsNullOrWhiteSpace(set) || !catalog.TryGetFiles(pack, set, out var files) || files.Count == 0) return null;
        var file = variations.Select($"{pack}/{set}", files, mapping.VariationMode);
        var variation = mapping.VolumeVariation == 0 ? 1f : 1f + ((Random.Shared.NextSingle() * 2 - 1) * mapping.VolumeVariation);
        var gain = Math.Clamp(GlobalVolume * profile.MasterGain * (keyOverride?.Gain ?? mapping.Gain) * variation, 0, 1);
        var pitchRange = keyOverride?.PitchVariation ?? mapping.PitchVariation;
        var pitch = pitchRange == 0 ? 0 : (Random.Shared.NextSingle() * 2 - 1) * pitchRange;
        return new SoundTrigger(new SoundId(pack, set, file), gain, pitch);
    }

    public void ClearState() => _pressed.Clear();
}
