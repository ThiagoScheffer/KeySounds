namespace KeySounds.Infrastructure.Persistence;

public sealed class StoragePaths
{
    public StoragePaths(string? root = null) => Root = root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KeySounds");
    public string Root { get; }
    public string SettingsFile => Path.Combine(Root, "settings.json");
    public string ProfilesDirectory => Path.Combine(Root, "profiles");
    public string SoundPacksDirectory => Path.Combine(Root, "soundpacks");
}
