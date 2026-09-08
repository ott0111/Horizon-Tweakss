using Horizon.Core.Interfaces;
using Microsoft.Win32;

namespace Horizon.Platform;

public sealed class WindowsLaunchAtStartupService : ILaunchAtStartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Horizon";

    public Task<bool> GetEnabledAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows()) return Task.FromResult(false);
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return Task.FromResult(key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value));
    }

    public Task SetEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Launch with Windows is available only on Windows.");

        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("Windows could not update the startup preference.");
        if (enabled)
        {
            var executablePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executablePath))
                throw new InvalidOperationException("Horizon could not locate its application file.");
            key.SetValue(ValueName, $"\"{executablePath}\"", RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        return Task.CompletedTask;
    }
}
