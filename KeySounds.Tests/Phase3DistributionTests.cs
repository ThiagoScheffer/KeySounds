using KeySounds.Application.Diagnostics;
using KeySounds.Application.Settings;
using KeySounds.Application.SystemIntegration;
using KeySounds.Core.Audio;
using KeySounds.Core.Settings;
using KeySounds.Infrastructure.Persistence;
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

    private sealed class FakeStartupStore : IStartupRegistrationStore
    {
        public Dictionary<string, string> Values { get; } = [];
        public void SetValue(string name, string value) => Values[name] = value;
        public void DeleteValue(string name) => Values.Remove(name);
        public string? GetValue(string name) => Values.TryGetValue(name, out var value) ? value : null;
    }
}
