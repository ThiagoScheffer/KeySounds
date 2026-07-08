using KeySounds.Application;
using KeySounds.Core.Input;
using KeySounds.Core.Settings;
using KeySounds.Infrastructure.Persistence;
using Xunit;

namespace KeySounds.Tests;

public sealed class PersistenceTests
{
    [Fact]
    public async Task AtomicSettingsWriteRecoversBackup()
    {
        var root = Path.Combine(Path.GetTempPath(), "KeySounds.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new StoragePaths(root); var writer = new AtomicFileWriter(); var repository = new JsonApplicationSettingsRepository(paths, writer);
            await repository.SaveAsync(new ApplicationSettings { GlobalVolume = .25f });
            await repository.SaveAsync(new ApplicationSettings { GlobalVolume = .75f });
            await File.WriteAllTextAsync(paths.SettingsFile, "not-json");
            var loaded = await repository.LoadAsync();
            Assert.Equal(.25f, loaded.Settings.GlobalVolume); Assert.NotNull(loaded.RecoveryMessage);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task ProfileRoundTripsPhysicalKeyOverride()
    {
        var root = Path.Combine(Path.GetTempPath(), "KeySounds.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new StoragePaths(root); var repository = new JsonProfileRepository(paths, new AtomicFileWriter());
            var profile = Phase2Defaults.CreateProfile(); profile.KeyOverrides[new KeyIdentifier(0x1E, true)] = new() { Enabled = false };
            await repository.SaveAsync(profile); var loaded = Assert.Single(await repository.ListAsync());
            Assert.False(loaded.KeyOverrides[new KeyIdentifier(0x1E, true)].Enabled);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
