using Microsoft.Win32;
using System.Runtime.Versioning;

namespace KeySounds.Application.SystemIntegration;

public interface IStartupRegistrationStore
{
    void SetValue(string name, string value);
    void DeleteValue(string name);
    string? GetValue(string name);
}

[SupportedOSPlatform("windows")]
public sealed class RegistryStartupRegistrationStore : IStartupRegistrationStore
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public void SetValue(string name, string value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, true) ?? throw new InvalidOperationException("Could not open the current-user Run key.");
        key.SetValue(name, value, RegistryValueKind.String);
    }
    public void DeleteValue(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
        key?.DeleteValue(name, false);
    }
    public string? GetValue(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
        return key?.GetValue(name) as string;
    }
}

public sealed class StartupRegistrationService(IStartupRegistrationStore store, string appName = "KeySounds")
{
    public string BuildCommandLine(string executablePath) => $"\"{Path.GetFullPath(executablePath)}\" --startup";
    public void SetEnabled(bool enabled, string executablePath)
    {
        if (enabled) store.SetValue(appName, BuildCommandLine(executablePath));
        else store.DeleteValue(appName);
    }
    public bool IsEnabledFor(string executablePath)
        => string.Equals(store.GetValue(appName), BuildCommandLine(executablePath), StringComparison.OrdinalIgnoreCase);
}
