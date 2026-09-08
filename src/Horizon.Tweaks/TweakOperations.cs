using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Horizon.Core.Interfaces;
using Horizon.Core.Models;

namespace Horizon.Tweaks;

public abstract class TweakOperationBase : ITweakOperation
{
    protected TweakOperationBase(string tweakId, string desiredDisplay) { TweakId = tweakId; DesiredDisplay = desiredDisplay; }
    public string TweakId { get; }
    public string DesiredDisplay { get; }
    public virtual Task<OperationCompatibility> CheckCompatibilityAsync(SystemSnapshot system, CancellationToken cancellationToken = default) =>
        Task.FromResult(system.PlatformSupported && OperatingSystem.IsWindows() ? new OperationCompatibility(true) : new OperationCompatibility(false, "Requires Windows 10 or Windows 11."));
    public abstract Task<TweakValue> ReadAsync(SystemSnapshot system, CancellationToken cancellationToken = default);
    public virtual Task<TweakValue> BackupAsync(TweakValue current, Guid sessionId, CancellationToken cancellationToken = default) => Task.FromResult(current);
    public abstract Task ApplyAsync(SystemSnapshot system, CancellationToken cancellationToken = default);
    public abstract bool IsDesired(TweakValue current);
    public abstract Task RestoreAsync(TweakValue previous, SystemSnapshot system, CancellationToken cancellationToken = default);
    public virtual bool IsRestored(TweakValue current, TweakValue previous) =>
        current.Exists == previous.Exists && (!previous.Exists || string.Equals(current.Value, previous.Value, StringComparison.OrdinalIgnoreCase));
}

public sealed class RegistryValueOperation : TweakOperationBase
{
    private readonly ICommandRunner _runner;
    private readonly string _hive;
    private readonly string _key;
    private readonly string _name;
    private readonly string _desired;
    private readonly string _valueType;
    private readonly bool _requiresAdmin;

    public RegistryValueOperation(ICommandRunner runner, string tweakId, string hive, string key, string name, object desired, string valueType = "DWord", bool requiresAdmin = false)
        : base(tweakId, Convert.ToString(desired, CultureInfo.InvariantCulture) ?? string.Empty)
    {
        _runner = runner; _hive = hive; _key = key; _name = name; _desired = DesiredDisplay; _valueType = valueType; _requiresAdmin = requiresAdmin;
    }

    private string RegistryPath => $"Registry::{_hive}\\{_key}";

    public override async Task<TweakValue> ReadAsync(SystemSnapshot system, CancellationToken cancellationToken = default)
    {
        var script = $"$p='{Ps(RegistryPath)}';$n='{Ps(_name)}';$r=if(Test-Path -LiteralPath $p){{$v=(Get-ItemProperty -LiteralPath $p -Name $n -ErrorAction SilentlyContinue).$n;if($null -ne $v){{@{{Exists=$true;Value=[string]$v}}}}else{{@{{Exists=$false;Value=$null}}}}}}else{{@{{Exists=$false;Value=$null}}}};$r|ConvertTo-Json -Compress";
        var result = await _runner.RunPowerShellAsync(script, cancellationToken: cancellationToken);
        using var json = JsonDocument.Parse(result.StandardOutput);
        var root = json.RootElement;
        var exists = root.TryGetProperty("Exists", out var existsValue) && existsValue.GetBoolean();
        var value = root.TryGetProperty("Value", out var valueElement) && valueElement.ValueKind != JsonValueKind.Null ? valueElement.ToString() : null;
        return new(exists, value, _valueType, $"{_hive}\\{_key}\\{_name}");
    }

    public override async Task ApplyAsync(SystemSnapshot system, CancellationToken cancellationToken = default)
    {
        var script = $"$p='{Ps(RegistryPath)}';New-Item -Path $p -Force|Out-Null;New-ItemProperty -LiteralPath $p -Name '{Ps(_name)}' -Value {ValueExpression(_desired)} -PropertyType {_valueType} -Force|Out-Null";
        await _runner.RunPowerShellAsync(script, _requiresAdmin && !system.IsAdministrator, cancellationToken: cancellationToken);
    }

    public override bool IsDesired(TweakValue current) => current.Exists && ValuesEqual(current.Value, _desired, _valueType);

    public override async Task RestoreAsync(TweakValue previous, SystemSnapshot system, CancellationToken cancellationToken = default)
    {
        var prefix = $"$p='{Ps(RegistryPath)}';$n='{Ps(_name)}';";
        var script = previous.Exists
            ? $"{prefix}New-Item -Path $p -Force|Out-Null;New-ItemProperty -LiteralPath $p -Name $n -Value {ValueExpression(previous.Value ?? string.Empty)} -PropertyType {_valueType} -Force|Out-Null"
            : $"{prefix}if(Test-Path -LiteralPath $p){{Remove-ItemProperty -LiteralPath $p -Name $n -ErrorAction SilentlyContinue}}";
        await _runner.RunPowerShellAsync(script, _requiresAdmin && !system.IsAdministrator, cancellationToken: cancellationToken);
    }

    private string ValueExpression(string value)
    {
        if (_valueType.Equals("DWord", StringComparison.OrdinalIgnoreCase) || _valueType.Equals("QWord", StringComparison.OrdinalIgnoreCase))
        {
            if (!ulong.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)) throw new InvalidOperationException("The captured registry number is invalid.");
            return number.ToString(CultureInfo.InvariantCulture);
        }
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
        return $"[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{encoded}'))";
    }

    private static bool ValuesEqual(string? left, string right, string type) =>
        type is "DWord" or "QWord" && ulong.TryParse(left, out var a) && ulong.TryParse(right, out var b) ? a == b : string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    private static string Ps(string value) => value.Replace("'", "''", StringComparison.Ordinal);
}

public sealed class PowerPlanOperation : TweakOperationBase
{
    private const string HighPerformance = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
    private readonly ICommandRunner _runner;
    public PowerPlanOperation(ICommandRunner runner, string tweakId) : base(tweakId, "High performance") => _runner = runner;

    public override async Task<TweakValue> ReadAsync(SystemSnapshot system, CancellationToken cancellationToken = default)
    {
        var result = await _runner.RunPowerShellAsync("$v=(powercfg /GETACTIVESCHEME|Out-String);if($v -match '([0-9a-fA-F-]{36})'){$matches[1].ToLower()}else{throw 'Unable to read the active power plan.'}", cancellationToken: cancellationToken);
        return new(true, result.StandardOutput.Trim(), "Guid", "Active power plan");
    }
    public override Task ApplyAsync(SystemSnapshot system, CancellationToken cancellationToken = default) => RunAsync("powercfg /SETACTIVE SCHEME_MIN|Out-Null", system, cancellationToken);
    public override bool IsDesired(TweakValue current) => string.Equals(current.Value, HighPerformance, StringComparison.OrdinalIgnoreCase);
    public override Task RestoreAsync(TweakValue previous, SystemSnapshot system, CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(previous.Value, out var scheme)) throw new InvalidOperationException("The captured power plan identifier is invalid.");
        return RunAsync($"powercfg /SETACTIVE {scheme:D}|Out-Null", system, cancellationToken);
    }
    private async Task RunAsync(string script, SystemSnapshot system, CancellationToken cancellationToken) =>
        _ = await _runner.RunPowerShellAsync(script, !system.IsAdministrator, cancellationToken: cancellationToken);
}

public sealed class ServiceStartOperation : TweakOperationBase
{
    private readonly ICommandRunner _runner;
    private readonly string _serviceName;
    private readonly string _startType;
    private readonly bool _stop;
    public ServiceStartOperation(ICommandRunner runner, string tweakId, string serviceName, string startType, bool stop)
        : base(tweakId, $"{startType}{(stop ? " / stopped" : string.Empty)}") { _runner = runner; _serviceName = serviceName; _startType = startType; _stop = stop; }

    public override async Task<OperationCompatibility> CheckCompatibilityAsync(SystemSnapshot system, CancellationToken cancellationToken = default)
    {
        var baseResult = await base.CheckCompatibilityAsync(system, cancellationToken);
        if (!baseResult.Supported) return baseResult;
        try { _ = await ReadAsync(system, cancellationToken); return new(true); }
        catch { return new(false, $"The {_serviceName} service is unavailable."); }
    }
    public override async Task<TweakValue> ReadAsync(SystemSnapshot system, CancellationToken cancellationToken = default)
    {
        var script = $"$s=Get-CimInstance Win32_Service -Filter \"Name='{Ps(_serviceName)}'\";if(!$s){{throw 'Service unavailable'}};@{{StartType=$s.StartMode;Running=($s.State -eq 'Running')}}|ConvertTo-Json -Compress";
        var result = await _runner.RunPowerShellAsync(script, cancellationToken: cancellationToken);
        using var json = JsonDocument.Parse(result.StandardOutput);
        var root = json.RootElement;
        return new(true, $"{root.GetProperty("StartType").GetString()}|{root.GetProperty("Running").GetBoolean()}", "Service", _serviceName);
    }
    public override bool IsDesired(TweakValue current)
    {
        var parts = current.Value?.Split('|') ?? [];
        return parts.Length == 2 && Normalize(parts[0]) == Normalize(_startType) && (!_stop || bool.TryParse(parts[1], out var running) && !running);
    }
    public override async Task ApplyAsync(SystemSnapshot system, CancellationToken cancellationToken = default)
    {
        var stop = _stop ? $"Stop-Service -Name '{Ps(_serviceName)}' -Force -ErrorAction SilentlyContinue;" : string.Empty;
        await _runner.RunPowerShellAsync($"{stop}Set-Service -Name '{Ps(_serviceName)}' -StartupType {_startType}", !system.IsAdministrator, cancellationToken: cancellationToken);
    }
    public override async Task RestoreAsync(TweakValue previous, SystemSnapshot system, CancellationToken cancellationToken = default)
    {
        var parts = previous.Value?.Split('|') ?? [];
        if (parts.Length != 2 || !bool.TryParse(parts[1], out var running)) throw new InvalidOperationException("The captured service state is invalid.");
        var startType = Normalize(parts[0]) switch { "automatic" => "Automatic", "disabled" => "Disabled", _ => "Manual" };
        var state = running ? $"Start-Service -Name '{Ps(_serviceName)}' -ErrorAction SilentlyContinue" : $"Stop-Service -Name '{Ps(_serviceName)}' -Force -ErrorAction SilentlyContinue";
        await _runner.RunPowerShellAsync($"Set-Service -Name '{Ps(_serviceName)}' -StartupType {startType};{state}", !system.IsAdministrator, cancellationToken: cancellationToken);
    }
    private static string Normalize(string value) => value.Equals("Auto", StringComparison.OrdinalIgnoreCase) ? "automatic" : value.ToLowerInvariant();
    private static string Ps(string value) => value.Replace("'", "''", StringComparison.Ordinal);
}

public sealed class NetshAutoTuningOperation : TweakOperationBase
{
    private readonly ICommandRunner _runner;
    public NetshAutoTuningOperation(ICommandRunner runner, string tweakId) : base(tweakId, "normal") => _runner = runner;
    public override async Task<TweakValue> ReadAsync(SystemSnapshot system, CancellationToken cancellationToken = default)
    {
        var result = await _runner.RunPowerShellAsync("netsh interface tcp show global", cancellationToken: cancellationToken);
        var match = Regex.Match(result.StandardOutput, @"Auto-Tuning[^:]*:\s*([\w-]+)", RegexOptions.IgnoreCase);
        if (!match.Success) throw new InvalidOperationException("Windows did not expose the TCP autotuning level in a supported format.");
        return new(true, match.Groups[1].Value.Trim().ToLowerInvariant(), "Netsh", "TCP receive-window autotuning");
    }
    public override bool IsDesired(TweakValue current) => string.Equals(current.Value, "normal", StringComparison.OrdinalIgnoreCase);
    public override async Task ApplyAsync(SystemSnapshot system, CancellationToken cancellationToken = default) =>
        _ = await _runner.RunPowerShellAsync("netsh interface tcp set global autotuninglevel=normal|Out-Null", !system.IsAdministrator, cancellationToken: cancellationToken);
    public override async Task RestoreAsync(TweakValue previous, SystemSnapshot system, CancellationToken cancellationToken = default)
    {
        if (!Regex.IsMatch(previous.Value ?? string.Empty, "^(disabled|highlyrestricted|restricted|normal|experimental)$", RegexOptions.IgnoreCase)) throw new InvalidOperationException("The captured TCP autotuning level is invalid.");
        await _runner.RunPowerShellAsync($"netsh interface tcp set global autotuninglevel={previous.Value}|Out-Null", !system.IsAdministrator, cancellationToken: cancellationToken);
    }
}

public sealed class FortniteIniOperation : TweakOperationBase
{
    private readonly string _section;
    private readonly string _key;
    private readonly string _desired;
    public FortniteIniOperation(string tweakId, string section, string key, string desired) : base(tweakId, desired) { _section = section; _key = key; _desired = desired; }
    private static string SettingsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FortniteGame", "Saved", "Config", "WindowsClient", "GameUserSettings.ini");

    public override Task<OperationCompatibility> CheckCompatibilityAsync(SystemSnapshot system, CancellationToken cancellationToken = default) =>
        Task.FromResult(File.Exists(SettingsPath) ? new OperationCompatibility(true) : new OperationCompatibility(false, "Fortnite GameUserSettings.ini was not detected."));
    public override async Task<TweakValue> ReadAsync(SystemSnapshot system, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(SettingsPath)) return TweakValue.Missing(SettingsPath);
        var content = await File.ReadAllTextAsync(SettingsPath, cancellationToken);
        var value = ReadIni(content, _section, _key);
        return new(value is not null, value, "Ini", SettingsPath);
    }
    public override async Task<TweakValue> BackupAsync(TweakValue current, Guid sessionId, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(SettingsPath)) return current;
        var directory = Path.Combine(Path.GetDirectoryName(SettingsPath)!, ".horizon-backups");
        Directory.CreateDirectory(directory);
        var backup = Path.Combine(directory, $"{sessionId:N}-{TweakId.Replace('.', '_')}.ini");
        await using var source = File.OpenRead(SettingsPath);
        await using var destination = File.Create(backup);
        await source.CopyToAsync(destination, cancellationToken);
        return current with { BackupPath = backup };
    }
    public override async Task ApplyAsync(SystemSnapshot system, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(SettingsPath)) throw new FileNotFoundException("Fortnite configuration was not found.", SettingsPath);
        var content = await File.ReadAllTextAsync(SettingsPath, cancellationToken);
        await File.WriteAllTextAsync(SettingsPath, WriteIni(content, _section, _key, _desired), cancellationToken);
    }
    public override bool IsDesired(TweakValue current) => current.Exists && string.Equals(current.Value, _desired, StringComparison.OrdinalIgnoreCase);
    public override async Task RestoreAsync(TweakValue previous, SystemSnapshot system, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(SettingsPath)) throw new FileNotFoundException("Fortnite configuration was removed after optimization.", SettingsPath);
        var content = await File.ReadAllTextAsync(SettingsPath, cancellationToken);
        var restored = previous.Exists ? WriteIni(content, _section, _key, previous.Value ?? string.Empty) : RemoveIni(content, _section, _key);
        await File.WriteAllTextAsync(SettingsPath, restored, cancellationToken);
    }
    private static string? ReadIni(string content, string section, string key)
    {
        var active = false;
        foreach (var line in content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var header = Regex.Match(line, @"^\s*\[([^\]]+)]\s*$");
            if (header.Success) { active = header.Groups[1].Value.Equals(section, StringComparison.OrdinalIgnoreCase); continue; }
            if (!active) continue;
            var pair = Regex.Match(line, @"^\s*([^=;#]+?)\s*=\s*(.*?)\s*$");
            if (pair.Success && pair.Groups[1].Value.Trim().Equals(key, StringComparison.OrdinalIgnoreCase)) return pair.Groups[2].Value;
        }
        return null;
    }
    private static string WriteIni(string content, string section, string key, string value)
    {
        var newline = content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = content.Split(["\r\n", "\n"], StringSplitOptions.None).ToList();
        var sectionIndex = lines.FindIndex(line => Regex.IsMatch(line, $@"^\s*\[{Regex.Escape(section)}]\s*$", RegexOptions.IgnoreCase));
        if (sectionIndex < 0) return content.TrimEnd() + newline + newline + $"[{section}]" + newline + $"{key}={value}" + newline;
        var nextSection = lines.FindIndex(sectionIndex + 1, line => Regex.IsMatch(line, @"^\s*\[[^\]]+]\s*$"));
        if (nextSection < 0) nextSection = lines.Count;
        for (var index = sectionIndex + 1; index < nextSection; index++)
        {
            var pair = Regex.Match(lines[index], @"^\s*([^=;#]+?)\s*=");
            if (!pair.Success || !pair.Groups[1].Value.Trim().Equals(key, StringComparison.OrdinalIgnoreCase)) continue;
            lines[index] = $"{key}={value}";
            return string.Join(newline, lines);
        }
        lines.Insert(nextSection, $"{key}={value}");
        return string.Join(newline, lines);
    }
    private static string RemoveIni(string content, string section, string key)
    {
        var newline = content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var active = false;
        var result = new List<string>();
        foreach (var line in content.Split(["\r\n", "\n"], StringSplitOptions.None))
        {
            var header = Regex.Match(line, @"^\s*\[([^\]]+)]\s*$");
            if (header.Success) active = header.Groups[1].Value.Equals(section, StringComparison.OrdinalIgnoreCase);
            var pair = Regex.Match(line, @"^\s*([^=;#]+?)\s*=");
            if (active && pair.Success && pair.Groups[1].Value.Trim().Equals(key, StringComparison.OrdinalIgnoreCase)) continue;
            result.Add(line);
        }
        return string.Join(newline, result);
    }
}

public sealed class TweakOperationRegistry : ITweakOperationRegistry
{
    private readonly Dictionary<string, ITweakOperation> _operations;
    public TweakOperationRegistry(ICommandRunner runner)
    {
        _operations = new(StringComparer.OrdinalIgnoreCase)
        {
            ["win.game-dvr"] = Reg(runner, "win.game-dvr", "HKEY_CURRENT_USER", "System\\GameConfigStore", "GameDVR_Enabled", 0),
            ["win.app-capture"] = Reg(runner, "win.app-capture", "HKEY_CURRENT_USER", "Software\\Microsoft\\Windows\\CurrentVersion\\GameDVR", "AppCaptureEnabled", 0),
            ["win.game-mode"] = Reg(runner, "win.game-mode", "HKEY_CURRENT_USER", "Software\\Microsoft\\GameBar", "AutoGameModeEnabled", 1),
            ["win.hags"] = Reg(runner, "win.hags", "HKEY_LOCAL_MACHINE", "SYSTEM\\CurrentControlSet\\Control\\GraphicsDrivers", "HwSchMode", 2, requiresAdmin: true),
            ["win.visual-effects"] = Reg(runner, "win.visual-effects", "HKEY_CURRENT_USER", "Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\VisualEffects", "VisualFXSetting", 2),
            ["win.background-apps"] = Reg(runner, "win.background-apps", "HKEY_CURRENT_USER", "Software\\Microsoft\\Windows\\CurrentVersion\\BackgroundAccessApplications", "GlobalUserDisabled", 1),
            ["win.notifications"] = Reg(runner, "win.notifications", "HKEY_CURRENT_USER", "Software\\Microsoft\\Windows\\CurrentVersion\\PushNotifications", "ToastEnabled", 0),
            ["win.taskbar-feeds"] = Reg(runner, "win.taskbar-feeds", "HKEY_CURRENT_USER", "Software\\Microsoft\\Windows\\CurrentVersion\\Feeds", "ShellFeedsTaskbarViewMode", 2),
            ["win.menu-delay"] = Reg(runner, "win.menu-delay", "HKEY_CURRENT_USER", "Control Panel\\Desktop", "MenuShowDelay", "100", "String"),
            ["win.process-scheduling"] = Reg(runner, "win.process-scheduling", "HKEY_LOCAL_MACHINE", "SYSTEM\\CurrentControlSet\\Control\\PriorityControl", "Win32PrioritySeparation", 38, requiresAdmin: true),
            ["privacy.telemetry"] = Reg(runner, "privacy.telemetry", "HKEY_LOCAL_MACHINE", "SOFTWARE\\Policies\\Microsoft\\Windows\\DataCollection", "AllowTelemetry", 1, requiresAdmin: true),
            ["input.mouse"] = Reg(runner, "input.mouse", "HKEY_CURRENT_USER", "Control Panel\\Mouse", "MouseSpeed", "0", "String"),
            ["perf.network-throttling"] = Reg(runner, "perf.network-throttling", "HKEY_LOCAL_MACHINE", "SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Multimedia\\SystemProfile", "NetworkThrottlingIndex", uint.MaxValue, requiresAdmin: true),
            ["perf.system-responsiveness"] = Reg(runner, "perf.system-responsiveness", "HKEY_LOCAL_MACHINE", "SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Multimedia\\SystemProfile", "SystemResponsiveness", 10, requiresAdmin: true),
            ["perf.games-priority"] = Reg(runner, "perf.games-priority", "HKEY_LOCAL_MACHINE", "SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Multimedia\\SystemProfile\\Tasks\\Games", "Priority", 6, requiresAdmin: true),
            ["power.throttling"] = Reg(runner, "power.throttling", "HKEY_LOCAL_MACHINE", "SYSTEM\\CurrentControlSet\\Control\\Power\\PowerThrottling", "PowerThrottlingOff", 1, requiresAdmin: true),
            ["power.performance"] = new PowerPlanOperation(runner, "power.performance"),
            ["net.autotuning"] = new NetshAutoTuningOperation(runner, "net.autotuning"),
            ["perf.disable-diagtrack"] = new ServiceStartOperation(runner, "perf.disable-diagtrack", "DiagTrack", "Disabled", true),
            ["fortnite.vsync"] = new FortniteIniOperation("fortnite.vsync", "/Script/FortniteGame.FortGameUserSettings", "bUseVSync", "False"),
            ["fortnite.grass"] = new FortniteIniOperation("fortnite.grass", "/Script/FortniteGame.FortGameUserSettings", "bShowGrass", "False"),
            ["fortnite.temporary-power"] = new PowerPlanOperation(runner, "fortnite.temporary-power"),
            ["fortnite.temporary-dvr"] = Reg(runner, "fortnite.temporary-dvr", "HKEY_CURRENT_USER", "System\\GameConfigStore", "GameDVR_Enabled", 0)
        };
    }
    public bool TryGet(string tweakId, out ITweakOperation operation) => _operations.TryGetValue(tweakId, out operation!);
    private static RegistryValueOperation Reg(ICommandRunner runner, string id, string hive, string key, string name, object value, string type = "DWord", bool requiresAdmin = false) => new(runner, id, hive, key, name, value, type, requiresAdmin);
}
