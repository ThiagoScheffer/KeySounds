using KeySounds.Application.Diagnostics;
using KeySounds.Application.Settings;
using KeySounds.Application.SystemIntegration;
using KeySounds.Core.Audio;
using KeySounds.Core.Settings;
using KeySounds.Infrastructure.Persistence;
using System.Text.Json;
using Xunit;

namespace KeySounds.Tests;

public sealed class Phase3DistributionTests
{
    [Fact]
    public void SettingsMigrationToSchema4PreservesPhase2AndAddsSafeDefaults()
    {
        var settings = new ApplicationSettings
        {
            SchemaVersion = 3,
            SoundsEnabled = false,
            ActiveProfileId = "custom",
            AudioOutputSelection = AudioOutputSelection.Specific("device"),
            KeyboardProfileAssignments = new(StringComparer.Ordinal) { ["kbd-v1-test"] = "custom" }
        };

        Assert.True(ApplicationSettingsMigration.Apply(settings));

        Assert.Equal(4, settings.SchemaVersion);
        Assert.False(settings.SoundsEnabled);
        Assert.Equal("custom", settings.ActiveProfileId);
        Assert.Equal(AudioOutputSelectionKind.SpecificDevice, settings.AudioOutputSelection.Kind);
        Assert.Equal("custom", settings.KeyboardProfileAssignments["kbd-v1-test"]);
        Assert.False(settings.StartWithWindows);
        Assert.True(settings.MinimizeToTray);
        Assert.True(settings.CloseToTray);
        Assert.True(settings.ShowWindowOnStartup);
        Assert.True(settings.EnableLocalCrashReports);
        Assert.Equal(14, settings.DiagnosticReportRetentionDays);
        Assert.False(ApplicationSettingsMigration.Apply(settings));
    }

    [Fact]
    public void StartupRegistrationWritesQuotedPerUserRunCommand()
    {
        var store = new FakeStartupStore();
        var service = new StartupRegistrationService(store);

        service.SetEnabled(true, @"C:\Program Files\KeySounds\KeySounds.exe");

        Assert.Equal(@"""C:\Program Files\KeySounds\KeySounds.exe"" --startup", store.Values["KeySounds"]);
        Assert.True(service.IsEnabledFor(@"C:\Program Files\KeySounds\KeySounds.exe"));
        service.SetEnabled(false, @"C:\Program Files\KeySounds\KeySounds.exe");
        Assert.False(store.Values.ContainsKey("KeySounds"));
    }

    [Fact]
    public async Task SingleInstanceSecondInstanceSignalsPrimaryActivation()
    {
        var name = Guid.NewGuid().ToString("N");
        using var first = new SingleInstanceCoordinator(name);
        using var second = new SingleInstanceCoordinator(name);
        var activated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        first.ActivationRequested += (_, _) => activated.TrySetResult();
        first.StartActivationServer();

        Assert.True(first.IsPrimary);
        Assert.False(second.IsPrimary);

        await second.SignalActivationAsync();
        await activated.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task SingleInstanceIgnoresUnknownCommandAndDisposalReleasesOwnership()
    {
        var name = Guid.NewGuid().ToString("N");
        using var first = new SingleInstanceCoordinator(name);
        using var second = new SingleInstanceCoordinator(name);
        var activated = false;
        first.ActivationRequested += (_, _) => activated = true;
        first.StartActivationServer();

        await second.SignalAsync("unknown-command");
        await Task.Delay(150);

        Assert.False(activated);
        first.Dispose();
        second.Dispose();
        using var replacement = new SingleInstanceCoordinator(name);
        Assert.True(replacement.IsPrimary);
    }

    [Fact]
    public void StartupRegistrationFailureLeavesNoPerUserValueWritten()
    {
        var store = new FailingStartupStore();
        var service = new StartupRegistrationService(store);

        Assert.Throws<UnauthorizedAccessException>(() => service.SetEnabled(true, @"C:\Program Files\KeySounds\KeySounds.exe"));
        Assert.Null(store.GetValue("KeySounds"));
    }

    [Fact]
    public async Task DiagnosticReportIncludesRequiredAggregateSectionsWithoutKeyboardData()
    {
        var root = Path.Combine(Path.GetTempPath(), "KeySounds.Phase3Diagnostics", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new StoragePaths(root);
            Directory.CreateDirectory(paths.Root);
            Directory.CreateDirectory(paths.ProfilesDirectory);
            Directory.CreateDirectory(paths.SoundPacksDirectory);
            await File.WriteAllTextAsync(paths.SettingsFile, """
            {
              "schemaVersion": 4,
              "soundsEnabled": true,
              "activeProfileId": "profile-secret",
              "audioOutputSelection": { "kind": "SystemDefault" },
              "audioLatencyMilliseconds": 20,
              "startWithWindows": false,
              "minimizeToTray": true,
              "closeToTray": true,
              "showWindowOnStartup": true,
              "keyboardProfileAssignments": {
                "kbd-v1-secret": "profile-secret"
              }
            }
            """);
            await File.WriteAllTextAsync(Path.Combine(paths.ProfilesDirectory, "profile-secret.json"), "{}");
            Directory.CreateDirectory(Path.Combine(paths.SoundPacksDirectory, "invalid-pack"));

            var service = new DiagnosticReportService(paths);
            var output = Path.Combine(root, "diagnostics.json");
            await service.ExportAsync(output);
            var json = await File.ReadAllTextAsync(output);
            using var document = JsonDocument.Parse(json);

            Assert.Equal(1, document.RootElement.GetProperty("ReportVersion").GetInt32());
            Assert.Equal(4, document.RootElement.GetProperty("SettingsSchemaVersion").GetInt32());
            Assert.Equal(1, document.RootElement.GetProperty("KeyboardAssignmentCount").GetInt32());
            Assert.Equal(1, document.RootElement.GetProperty("ProfileFileCount").GetInt32());
            Assert.Equal(1, document.RootElement.GetProperty("InvalidSoundPackDirectoryCount").GetInt32());
            Assert.DoesNotContain("kbd-v1-secret", json);
            Assert.DoesNotContain("profile-secret", json);
            Assert.DoesNotContain("scanCode", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("virtualKey", json, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task LocalErrorReportHonorsSettingAndSanitizesUserName()
    {
        var root = Path.Combine(Path.GetTempPath(), "KeySounds.Phase3Reports", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new StoragePaths(root);
            var service = new LocalErrorReportService(paths);
            var settings = new ApplicationSettings { EnableLocalCrashReports = true, DiagnosticReportRetentionDays = 14 };

            var path = await service.WriteAsync(new InvalidOperationException("Failure for " + Environment.UserName), settings, "Test", "Unit");

            Assert.NotNull(path);
            var json = await File.ReadAllTextAsync(path!);
            Assert.Contains("InvalidOperationException", json);
            Assert.DoesNotContain(Environment.UserName, json);

            settings.EnableLocalCrashReports = false;
            Assert.Null(await service.WriteAsync(new Exception("ignored"), settings, "Test"));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void LocalErrorReportCleanupRemovesExpiredReports()
    {
        var root = Path.Combine(Path.GetTempPath(), "KeySounds.Phase3Reports", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new StoragePaths(root);
            var service = new LocalErrorReportService(paths);
            Directory.CreateDirectory(service.ReportsDirectory);
            var oldPath = Path.Combine(service.ReportsDirectory, "error-old.json");
            File.WriteAllText(oldPath, "{}");
            File.SetCreationTimeUtc(oldPath, DateTime.UtcNow.AddDays(-30));

            service.Cleanup(14);

            Assert.False(File.Exists(oldPath));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private sealed class FakeStartupStore : IStartupRegistrationStore
    {
        public Dictionary<string, string> Values { get; } = [];
        public void SetValue(string name, string value) => Values[name] = value;
        public void DeleteValue(string name) => Values.Remove(name);
        public string? GetValue(string name) => Values.TryGetValue(name, out var value) ? value : null;
    }

    private sealed class FailingStartupStore : IStartupRegistrationStore
    {
        public void SetValue(string name, string value) => throw new UnauthorizedAccessException("Registry denied.");
        public void DeleteValue(string name) { }
        public string? GetValue(string name) => null;
    }
}
