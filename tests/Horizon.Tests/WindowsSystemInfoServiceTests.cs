using Horizon.Core.Interfaces;
using Horizon.Core.Models;
using Horizon.Platform;
using Xunit;

namespace Horizon.Tests;

public sealed class WindowsSystemInfoServiceTests
{
    [Fact]
    public async Task Scan_coalesces_concurrent_requests_and_reuses_the_recent_snapshot()
    {
        var runner = new CountingSystemRunner();
        var service = new WindowsSystemInfoService(runner);

        var first = service.ScanAsync();
        var second = service.ScanAsync();
        await Task.WhenAll(first, second);
        var third = await service.ScanAsync();

        Assert.Equal(1, runner.ScanCount);
        Assert.Equal((await first).DetectedAt, (await second).DetectedAt);
        Assert.Equal((await first).DetectedAt, third.DetectedAt);
    }

    [Fact]
    public async Task Scan_accepts_null_optional_hardware_numbers()
    {
        var service = new WindowsSystemInfoService(new NullHardwareValueRunner());

        var snapshot = await service.ScanAsync();

        Assert.Equal("Test CPU", snapshot.Cpu);
        Assert.Equal("16 GB", snapshot.Ram);
        Assert.Equal("1600 MT/s", snapshot.MemorySpeed);
    }

    private sealed class CountingSystemRunner : ICommandRunner
    {
        public bool IsWindows => true;
        public int ScanCount { get; private set; }

        public async Task<CommandResult> RunPowerShellAsync(
            string script,
            bool elevated = false,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
        {
            ScanCount++;
            await Task.Delay(20, cancellationToken);
            return new CommandResult(0, "{}", string.Empty);
        }
    }

    private sealed class NullHardwareValueRunner : ICommandRunner
    {
        public bool IsWindows => true;

        public Task<CommandResult> RunPowerShellAsync(
            string script,
            bool elevated = false,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new CommandResult(0,
                """{"Device":"Test PC","Edition":"Windows 11","Build":"1","Version":"1","IsAdmin":false,"Cpu":"Test CPU","Cores":4,"Threads":8,"Gpus":[],"Memory":[{"Capacity":17179869184,"Speed":1600,"ConfiguredSpeed":null,"Manufacturer":null,"PartNumber":null}],"PhysicalDisks":[],"Volumes":[],"Network":[],"InputDevices":[],"Motherboard":null,"Bios":null,"Games":{},"Software":{}}""",
                string.Empty));
    }
}
