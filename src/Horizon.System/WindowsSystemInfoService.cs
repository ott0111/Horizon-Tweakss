using System.Text.Json;
using Horizon.Core.Interfaces;
using Horizon.Core.Models;

namespace Horizon.Platform;

public sealed class WindowsSystemInfoService : ISystemInfoService
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(5);
    private readonly ICommandRunner _runner;
    private readonly IAppLogger? _logger;
    private readonly SemaphoreSlim _scanGate = new(1, 1);
    private SystemSnapshot? _cache;

    public WindowsSystemInfoService(ICommandRunner runner, IAppLogger? logger = null)
    {
        _runner = runner;
        _logger = logger;
    }

    public async Task<SystemSnapshot> ScanAsync(CancellationToken cancellationToken = default)
    {
        if (TryGetFreshSnapshot(out var cached)) return cached;

        await _scanGate.WaitAsync(cancellationToken);
        try
        {
            if (TryGetFreshSnapshot(out cached)) return cached;
            return await ScanUncachedAsync(cancellationToken);
        }
        finally
        {
            _scanGate.Release();
        }
    }

    public async Task<SystemSnapshot> RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _scanGate.WaitAsync(cancellationToken);
        try
        {
            return await ScanUncachedAsync(cancellationToken);
        }
        finally
        {
            _scanGate.Release();
        }
    }

    private async Task<SystemSnapshot> ScanUncachedAsync(CancellationToken cancellationToken)
    {
        if (!_runner.IsWindows)
            return SystemSnapshot.Unknown with
            {
                DeviceName = Environment.MachineName,
                WindowsEdition = Environment.OSVersion.VersionString,
                PlatformSupported = false,
                DetectedAt = DateTimeOffset.UtcNow
            };

        try
        {
            var result = await _runner.RunPowerShellAsync(DetectionScript, timeout: TimeSpan.FromSeconds(20), cancellationToken: cancellationToken);
            using var document = JsonDocument.Parse(result.StandardOutput);
            var root = document.RootElement;
            var gpus = ReadArray(root, "Gpus", item => new GpuSnapshot(
                Get(item, "Name", "Unknown GPU"), Get(item, "Vendor", "other"), Get(item, "Driver", "—"), GetLong(item, "Vram")));
            var memory = ReadArray(root, "Memory", item => new MemoryModuleSnapshot(
                GetLong(item, "Capacity"), GetInt(item, "Speed"), GetInt(item, "ConfiguredSpeed"), Get(item, "Manufacturer", "—"), Get(item, "PartNumber", "—")));
            var physical = ReadArray(root, "PhysicalDisks", item => new DriveSnapshot(
                Get(item, "Name", "Unknown drive"), Get(item, "MediaType", "Unspecified"), Get(item, "BusType", "Unknown"), Get(item, "Health", "Unknown"), GetLong(item, "Size"), null, 0));
            var volumes = ReadArray(root, "Volumes", item => new DriveSnapshot(
                Get(item, "Label", Get(item, "Name", "Volume")), "Volume", Get(item, "FileSystem", "Unknown"), "Available", GetLong(item, "Size"), Get(item, "Name", null), GetLong(item, "Free")));
            var drives = physical.Concat(volumes).ToList();
            var adapters = ReadArray(root, "Network", item => new NetworkAdapterSnapshot(
                Get(item, "Name", "Adapter"), Get(item, "Description", "—"), Get(item, "Guid", "—"), Get(item, "LinkSpeed", "—"), Get(item, "Driver", "—"), Get(item, "MediaType", "—")));
            var inputs = ReadArray(root, "InputDevices", item => new InputDeviceSnapshot(
                Get(item, "Name", Get(item, "InstanceId", "Input device")), Get(item, "Class", "HID"), Get(item, "InstanceId", "—"), Get(item, "Status", "Unknown")));
            var games = ReadDictionary(root, "Games");
            var software = ReadDictionary(root, "Software");
            var primaryGpu = gpus.FirstOrDefault() ?? new("Unknown GPU", "other", "—", 0);
            var totalRam = memory.Sum(module => module.CapacityBytes);
            var primaryVolume = volumes.OrderByDescending(volume => volume.SizeBytes).FirstOrDefault();
            var totalStorage = volumes.Sum(volume => volume.SizeBytes);
            var totalFree = volumes.Sum(volume => volume.FreeBytes);
            _cache = new(
                Get(root, "Device", Environment.MachineName), Get(root, "Edition", "Windows"), Get(root, "Build", "—"),
                Get(root, "Cpu", "Unknown CPU"), GetInt(root, "Cores"), GetInt(root, "Threads"),
                primaryGpu.Name, primaryGpu.Driver, SizeFormatter.Format(primaryGpu.VramBytes), SizeFormatter.Format(totalRam),
                memory.Count > 0 ? $"{memory.Max(module => module.ConfiguredSpeed > 0 ? module.ConfiguredSpeed : module.Speed)} MT/s" : "—",
                SizeFormatter.Format(totalStorage > 0 ? totalStorage : primaryVolume?.SizeBytes ?? 0), SizeFormatter.Format(totalFree > 0 ? totalFree : primaryVolume?.FreeBytes ?? 0),
                adapters.FirstOrDefault()?.Name ?? "Not detected", Get(root, "Motherboard", "Not detected"), Get(root, "Bios", "Not detected"), games.ContainsKey("fortnite"),
                Get(root, "Version", "—"), GetBool(root, "IsAdmin"), gpus, memory, drives, adapters, inputs, games, software, DateTimeOffset.UtcNow, true);
            return _cache;
        }
        catch (Exception exception)
        {
            _logger?.Error("system.scan", exception, nameof(WindowsSystemInfoService));
            return _cache ?? SystemSnapshot.Unknown with
            {
                DeviceName = Environment.MachineName,
                WindowsEdition = Environment.OSVersion.VersionString,
                PlatformSupported = true,
                DetectedAt = DateTimeOffset.UtcNow
            };
        }
    }

    private bool TryGetFreshSnapshot(out SystemSnapshot snapshot)
    {
        snapshot = _cache!;
        return snapshot is not null && DateTimeOffset.UtcNow - snapshot.DetectedAt < CacheLifetime;
    }

    private const string DetectionScript = """
        $ErrorActionPreference='SilentlyContinue'
        $os=Get-CimInstance Win32_OperatingSystem
        $cpu=Get-CimInstance Win32_Processor | Select-Object -First 1
        $gpus=@(Get-CimInstance Win32_VideoController | ForEach-Object { [ordered]@{Name=$_.Name;Driver=$_.DriverVersion;Vram=[int64]$_.AdapterRAM;Vendor=if($_.Name -match 'NVIDIA'){'nvidia'}elseif($_.Name -match 'AMD|Radeon'){'amd'}elseif($_.Name -match 'Intel'){'intel'}else{'other'}} })
        $memory=@(Get-CimInstance Win32_PhysicalMemory | ForEach-Object { [ordered]@{Capacity=[int64]$_.Capacity;Speed=$_.Speed;ConfiguredSpeed=$_.ConfiguredClockSpeed;Manufacturer=$_.Manufacturer;PartNumber=([string]$_.PartNumber).Trim()} })
        # Storage-module and full PnP enumeration can stall for tens of seconds on otherwise healthy PCs.
        # Logical volumes provide the capacity data Horizon needs without loading those slow providers.
        $physical=@()
        $volumes=@(Get-CimInstance Win32_LogicalDisk -Filter 'DriveType=3' | ForEach-Object { [ordered]@{Name=$_.DeviceID;Label=$_.VolumeName;FileSystem=$_.FileSystem;Size=[int64]$_.Size;Free=[int64]$_.FreeSpace} })
        $network=@(Get-CimInstance Win32_NetworkAdapter -Filter 'NetEnabled=True' | ForEach-Object { [ordered]@{Name=$_.NetConnectionID;Description=$_.Name;Guid=$_.GUID;LinkSpeed=if($_.Speed){'{0:0.##} Gbps' -f ([double]$_.Speed/1gb)}else{'—'};Driver=$_.DriverVersion;MediaType=$_.AdapterType} })
        $input=@()
        $board=Get-CimInstance Win32_BaseBoard | Select-Object -First 1
        $bios=Get-CimInstance Win32_BIOS | Select-Object -First 1
        $principal=New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
        $admin=$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
        $pf86=[Environment]::GetFolderPath('ProgramFilesX86')
        $games=[ordered]@{}
        $candidates=[ordered]@{
          fortnite=@("$env:ProgramFiles\Epic Games\Fortnite","$env:ProgramFiles\Fortnite","$env:LOCALAPPDATA\FortniteGame\Saved\Config\WindowsClient\GameUserSettings.ini");
          valorant=@("$env:SystemDrive\Riot Games\VALORANT","$env:LOCALAPPDATA\VALORANT");
          callOfDuty=@("$pf86\Call of Duty","$env:USERPROFILE\Documents\Call of Duty");
          apex=@("$pf86\Steam\steamapps\common\Apex Legends","$env:ProgramFiles\EA Games\Apex");
          cs2=@("$pf86\Steam\steamapps\common\Counter-Strike Global Offensive");
          rocketLeague=@("$env:ProgramFiles\Epic Games\rocketleague","$env:USERPROFILE\Documents\My Games\Rocket League")
        }
        foreach($game in $candidates.Keys){$found=$candidates[$game]|Where-Object{Test-Path $_}|Select-Object -First 1;if($found){$games[$game]=$found}}
        $software=[ordered]@{}
        $softwareCandidates=[ordered]@{discord=@("$env:LOCALAPPDATA\Discord","$env:APPDATA\discord");epicGames=@("$pf86\Epic Games\Launcher");obs=@("$env:APPDATA\obs-studio")}
        foreach($name in $softwareCandidates.Keys){$found=$softwareCandidates[$name]|Where-Object{Test-Path $_}|Select-Object -First 1;if($found){$software[$name]=$found}}
        [ordered]@{Device=$env:COMPUTERNAME;Edition=$os.Caption;Version=$os.Version;Build=$os.BuildNumber;IsAdmin=$admin;Cpu=$cpu.Name;Cores=$cpu.NumberOfCores;Threads=$cpu.NumberOfLogicalProcessors;Gpus=$gpus;Memory=$memory;PhysicalDisks=$physical;Volumes=$volumes;Network=$network;InputDevices=$input;Motherboard=("$($board.Manufacturer) $($board.Product)").Trim();Bios=$bios.SMBIOSBIOSVersion;Games=$games;Software=$software}|ConvertTo-Json -Depth 8 -Compress
        """;

    private static List<T> ReadArray<T>(JsonElement root, string name, Func<JsonElement, T> selector)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return [];
        return value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().Select(selector).ToList() : [selector(value)];
    }

    private static Dictionary<string, string> ReadDictionary(JsonElement root, string name)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Object) return result;
        foreach (var property in value.EnumerateObject()) if (property.Value.ValueKind == JsonValueKind.String) result[property.Name] = property.Value.GetString()!;
        return result;
    }

    private static string Get(JsonElement root, string name, string? fallback) => root.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null ? value.ToString() : fallback ?? string.Empty;
    private static int GetInt(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var result) ? result : 0;
    private static long GetLong(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var result) ? result : 0;
    private static bool GetBool(JsonElement root, string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}
