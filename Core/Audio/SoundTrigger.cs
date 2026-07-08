namespace KeySounds.Core.Audio;

public readonly record struct SoundId(string PackId, string SoundSetId, string FileId);

public readonly record struct SoundTrigger(SoundId SoundId, float Gain, float PitchSemitones);

public sealed record AudioOutputDevice(string Id, string Name, bool IsDefault, bool IsAvailable = true);
