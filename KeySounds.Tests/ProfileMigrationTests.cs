using KeySounds.Application;
using KeySounds.Application.Profiles;
using KeySounds.Core.Input;
using KeySounds.Infrastructure.Persistence;
using Xunit;

namespace KeySounds.Tests;

public sealed class ProfileMigrationTests
{
    [Fact]
    public async Task LegacyFallbackReferenceMigratesOnceAndCreatesBackup()
    {
        var root = Path.Combine(Path.GetTempPath(), "KeySounds.MigrationTests", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new StoragePaths(root); var repository = new JsonProfileRepository(paths, new AtomicFileWriter());
            var profile = Phase2Defaults.CreateProfile(); profile.Groups[KeySoundGroup.Alpha].SoundPackId = "fallback";
            await repository.SaveAsync(profile);
            var migration = new FallbackProfileMigrationService(repository);
            Assert.True(await migration.MigrateAsync(profile)); Assert.Equal("builtin", profile.Groups[KeySoundGroup.Alpha].SoundPackId);
            Assert.True(File.Exists(Path.Combine(paths.ProfilesDirectory, "default.json.backup")));
            Assert.False(await migration.MigrateAsync(profile));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task DefaultProfileWithNoAudibleRouteIsRepaired()
    {
        var root = Path.Combine(Path.GetTempPath(), "KeySounds.MigrationTests", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new StoragePaths(root); var repository = new JsonProfileRepository(paths, new AtomicFileWriter());
            var profile = Phase2Defaults.CreateProfile();
            profile.PlayOnKeyDown = false;
            profile.PlayOnKeyUp = true;
            foreach (var mapping in profile.Groups.Values) mapping.KeyUpSoundSetId = null;
            await repository.SaveAsync(profile);

            var migration = new FallbackProfileMigrationService(repository);

            Assert.True(await migration.MigrateAsync(profile));
            Assert.True(profile.PlayOnKeyDown);
            Assert.False(profile.PlayOnKeyUp);
            Assert.All(profile.Groups.Values, mapping => Assert.Equal("click", mapping.KeyDownSoundSetId));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
