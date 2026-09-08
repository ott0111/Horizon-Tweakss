using Horizon.Core.Interfaces;
using Horizon.Core.Models;
using Horizon.Tweaks;
using Xunit;

namespace Horizon.Tests;

public sealed class TweakOperationTests
{
    [Fact]
    public async Task Registry_operation_reads_applies_verifies_and_restores_missing_value()
    {
        var runner = new RegistryRunner();
        var operation = new RegistryValueOperation(runner, "test.registry", "HKEY_CURRENT_USER", "Software\\HorizonTests", "Value", 1);
        var system = SystemSnapshot.Unknown with { PlatformSupported = true };

        var before = await operation.ReadAsync(system);
        Assert.False(before.Exists);
        Assert.False(operation.IsDesired(before));

        var backup = await operation.BackupAsync(before, Guid.NewGuid());
        await operation.ApplyAsync(system);
        var applied = await operation.ReadAsync(system);
        Assert.True(operation.IsDesired(applied));

        await operation.RestoreAsync(backup, system);
        var restored = await operation.ReadAsync(system);
        Assert.True(operation.IsRestored(restored, backup));
        Assert.Equal(1, runner.ApplyCount);
        Assert.Equal(1, runner.RestoreCount);
    }

    [Fact]
    public void Catalogue_contains_all_package_tiers_and_separate_gpu_vendor_paths()
    {
        var tweaks = new HorizonTweakCatalog().GetAll();
        Assert.Contains(tweaks, tweak => tweak.RequiredPlan == PlanTier.Starter);
        Assert.Contains(tweaks, tweak => tweak.RequiredPlan == PlanTier.Performance);
        Assert.Contains(tweaks, tweak => tweak.RequiredPlan == PlanTier.Ultimate);
        Assert.Contains(tweaks, tweak => tweak.Compatibility == "nvidia");
        Assert.Contains(tweaks, tweak => tweak.Compatibility == "amd");
        Assert.Contains(tweaks, tweak => tweak.Temporary && tweak.Id.StartsWith("fortnite", StringComparison.Ordinal));
        Assert.Contains(tweaks, tweak => tweak.RequiredEntitlement == "bios");
        Assert.Contains(tweaks, tweak => tweak.RequiredEntitlement == "ram");
        Assert.Contains(tweaks, tweak => tweak.RequiredEntitlement == "cpu");
        Assert.Contains(tweaks, tweak => tweak.RequiredEntitlement == "gpu");
        Assert.Contains(tweaks, tweak => tweak.RequiredEntitlement == "network");
    }

    private sealed class RegistryRunner : ICommandRunner
    {
        private bool _exists;
        public bool IsWindows => true;
        public int ApplyCount { get; private set; }
        public int RestoreCount { get; private set; }

        public Task<CommandResult> RunPowerShellAsync(string script, bool elevated = false, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        {
            if (script.Contains("ConvertTo-Json", StringComparison.Ordinal))
                return Task.FromResult(new CommandResult(0, _exists ? "{\"Exists\":true,\"Value\":\"1\"}" : "{\"Exists\":false,\"Value\":null}", string.Empty));
            if (script.Contains("Remove-ItemProperty", StringComparison.Ordinal)) { _exists = false; RestoreCount++; }
            else if (script.Contains("New-ItemProperty", StringComparison.Ordinal)) { _exists = true; ApplyCount++; }
            return Task.FromResult(new CommandResult(0, string.Empty, string.Empty));
        }
    }
}
