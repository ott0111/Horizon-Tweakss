using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Horizon.Core.Interfaces;
using Horizon.Core.Models;

namespace Horizon.Services;

public sealed class CleanupService : ICleanupService
{
    private static readonly IReadOnlyDictionary<string, string[]> Roots = BuildRoots();

    public async Task<IReadOnlyList<CleanupCategory>> ScanAsync(IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        var definitions = new[]
        {
            ("temp", "Temporary Files", "Safe temporary data; locked files are skipped.", "\uF0FF"),
            ("shader", "Shader Caches", "Windows, NVIDIA, and AMD shader caches that are rebuilt when needed.", "\uE3A5"),
            ("crash", "Crash Reports", "Application crash dumps and Windows error reports.", "\uE868"),
            ("windows-temp", "Windows Temp", "Windows temporary data that is not currently in use.", "\uEB34"),
            ("game", "Game & Launcher Caches", "Detected Epic and Discord caches that can be regenerated.", "\uEA28")
        };
        if (!OperatingSystem.IsWindows()) return [];
        var results = new List<CleanupCategory>();
        for (var index = 0; index < definitions.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var definition = definitions[index];
            long size = 0;
            foreach (var root in Roots[definition.Item1].Distinct(StringComparer.OrdinalIgnoreCase))
                size += await DirectorySizeAsync(root, cancellationToken);
            results.Add(new() { Id = definition.Item1, Name = definition.Item2, Description = definition.Item3, Icon = definition.Item4, DetectedBytes = size });
            progress?.Report((index + 1d) / definitions.Length);
        }
        return results;
    }

    public async Task<long> CleanAsync(IEnumerable<CleanupCategory> categories, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Cleanup requires Windows.");
        var selected = categories.Where(category => category.IsSelected && Roots.ContainsKey(category.Id)).ToList();
        long removed = 0;
        for (var index = 0; index < selected.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var root in Roots[selected[index].Id].Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!Directory.Exists(root)) continue;
                foreach (var entry in Directory.EnumerateFileSystemEntries(root).ToList())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var before = await EntrySizeAsync(entry, cancellationToken);
                    try
                    {
                        if (Directory.Exists(entry)) Directory.Delete(entry, true); else File.Delete(entry);
                        removed += before;
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
            progress?.Report((index + 1d) / Math.Max(1, selected.Count));
        }
        return removed;
    }

    private static IReadOnlyDictionary<string, string[]> BuildRoots()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        return new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["temp"] = [Path.GetTempPath()],
            ["shader"] = [Path.Combine(local, "D3DSCache"), Path.Combine(local, "NVIDIA", "DXCache"), Path.Combine(local, "AMD", "DxCache")],
            ["crash"] = [Path.Combine(local, "CrashDumps"), Path.Combine(local, "Microsoft", "Windows", "WER")],
            ["windows-temp"] = [Path.Combine(windows, "Temp")],
            ["game"] = [Path.Combine(local, "EpicGamesLauncher", "Saved", "webcache"), Path.Combine(roaming, "discord", "Cache")]
        };
    }

    internal static Task<long> DirectorySizeAsync(string path, CancellationToken cancellationToken) => EntrySizeAsync(path, cancellationToken);
    private static Task<long> EntrySizeAsync(string path, CancellationToken cancellationToken) => Task.Run(() =>
    {
        if (!File.Exists(path) && !Directory.Exists(path)) return 0L;
        long total = 0;
        var pending = new Stack<string>(); pending.Push(path);
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = pending.Pop();
            try
            {
                var attributes = File.GetAttributes(current);
                if (attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                if (!attributes.HasFlag(FileAttributes.Directory)) { total += new FileInfo(current).Length; continue; }
                foreach (var child in Directory.EnumerateFileSystemEntries(current)) pending.Push(child);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return total;
    }, cancellationToken);
}

public sealed class DebloatService : IDebloatService
{
    private static readonly Regex Keep = new("Store|DesktopAppInstaller|WindowsCalculator|WindowsTerminal|SecHealthUI|VCLibs|NET\\.Native|UI\\.Xaml|Photos", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Recommended = new("BingNews|BingWeather|GetHelp|Getstarted|MicrosoftSolitaireCollection|People|WindowsMaps|ZuneMusic|ZuneVideo|YourPhone|Clipchamp", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private readonly ICommandRunner _runner;
    public DebloatService(ICommandRunner runner) => _runner = runner;

    public async Task<IReadOnlyList<InstalledPackage>> GetInstalledPackagesAsync(CancellationToken cancellationToken = default)
    {
        if (!_runner.IsWindows) return [];
        var result = await _runner.RunPowerShellAsync("Get-AppxPackage|Select-Object Name,PackageFullName,Publisher,Version,IsFramework,NonRemovable|ConvertTo-Json -Depth 3 -Compress", cancellationToken: cancellationToken);
        if (string.IsNullOrWhiteSpace(result.StandardOutput)) return [];
        using var document = JsonDocument.Parse(result.StandardOutput);
        var elements = document.RootElement.ValueKind == JsonValueKind.Array ? document.RootElement.EnumerateArray().ToList() : [document.RootElement];
        return elements.Select(item =>
        {
            var name = Get(item, "Name");
            var nonRemovable = GetBool(item, "NonRemovable");
            var framework = GetBool(item, "IsFramework");
            var group = nonRemovable || framework || Keep.IsMatch(name) ? "Keep" : Recommended.IsMatch(name) ? "Recommended" : "Optional";
            return new InstalledPackage(Get(item, "PackageFullName"), name, Get(item, "Publisher"), group == "Recommended", false, !(nonRemovable || framework || group == "Keep"), group);
        }).OrderBy(package => package.Group).ThenBy(package => package.Name).ToList();
    }

    public async Task RemoveAsync(IEnumerable<string> packageIds, CancellationToken cancellationToken = default)
    {
        var inventory = (await GetInstalledPackagesAsync(cancellationToken)).Where(package => package.Removable).ToDictionary(package => package.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var packageId in packageIds.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!inventory.ContainsKey(packageId)) throw new InvalidOperationException("A selected package is protected or changed after the scan.");
            var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(packageId));
            await _runner.RunPowerShellAsync($"$p=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{encoded}'));Remove-AppxPackage -Package $p -ErrorAction Stop", cancellationToken: cancellationToken);
        }
    }
    private static string Get(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null ? value.ToString() : string.Empty;
    private static bool GetBool(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}

public sealed class StartupService : IStartupService
{
    private readonly ICommandRunner _runner;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private static string BackupFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Horizon", "startup-backups.json");
    public StartupService(ICommandRunner runner) => _runner = runner;

    public async Task<IReadOnlyList<StartupApplication>> GetApplicationsAsync(CancellationToken cancellationToken = default)
    {
        if (!_runner.IsWindows) return [];
        var detected = await ScanDetectedAsync(cancellationToken);
        var backups = await ReadBackupsAsync(cancellationToken);
        detected.AddRange(backups.Select(pair => pair.Value.Application with { Enabled = false, BackupId = pair.Key }));
        return detected.OrderBy(application => application.Name).ToList();
    }

    private async Task<List<StartupApplication>> ScanDetectedAsync(CancellationToken cancellationToken)
    {
        var result = await _runner.RunPowerShellAsync(StartupScanScript, cancellationToken: cancellationToken);
        var detected = new List<StartupApplication>();
        if (!string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            using var document = JsonDocument.Parse(result.StandardOutput);
            var elements = document.RootElement.ValueKind == JsonValueKind.Array ? document.RootElement.EnumerateArray().ToList() : [document.RootElement];
            detected.AddRange(elements.Select(ParseStartup));
        }
        return detected;
    }

    public async Task SetEnabledAsync(string id, bool enabled, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var backups = await ReadBackupsUnsafeAsync(cancellationToken);
            if (enabled)
            {
                var backup = backups.Values.FirstOrDefault(item => item.Application.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
                if (backup is null) throw new InvalidOperationException("The startup backup was not found.");
                await RestoreAsync(backup.Application, cancellationToken);
                var key = backups.First(pair => pair.Value == backup).Key;
                backups.Remove(key);
                await WriteBackupsUnsafeAsync(backups, cancellationToken);
                return;
            }
            var current = await ScanDetectedAsync(cancellationToken);
            var application = current.FirstOrDefault(item => item.Enabled && item.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidOperationException("The startup item changed after the scan.");
            var disabled = await DisableAsync(application, cancellationToken);
            var backupId = Guid.NewGuid().ToString("N");
            backups[backupId] = new(disabled, DateTimeOffset.UtcNow);
            await WriteBackupsUnsafeAsync(backups, cancellationToken);
        }
        finally { _gate.Release(); }
    }

    private async Task<StartupApplication> DisableAsync(StartupApplication application, CancellationToken cancellationToken)
    {
        if (application.Kind == "registry")
        {
            var name = application.Id[(application.Id.LastIndexOf('|') + 1)..];
            await _runner.RunPowerShellAsync($"Remove-ItemProperty -LiteralPath '{Ps(application.Location)}' -Name '{Ps(name)}' -ErrorAction Stop", application.RequiresAdmin, cancellationToken: cancellationToken);
            return application;
        }
        var disabledPath = application.Location + ".horizon-disabled";
        if (application.RequiresAdmin) await _runner.RunPowerShellAsync($"Move-Item -LiteralPath '{Ps(application.Location)}' -Destination '{Ps(disabledPath)}' -Force", true, cancellationToken: cancellationToken);
        else File.Move(application.Location, disabledPath, true);
        return application with { Command = disabledPath };
    }

    private async Task RestoreAsync(StartupApplication application, CancellationToken cancellationToken)
    {
        if (application.Kind == "registry")
        {
            var name = application.Id[(application.Id.LastIndexOf('|') + 1)..];
            var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(application.Command));
            await _runner.RunPowerShellAsync($"$v=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{encoded}'));New-Item -Path '{Ps(application.Location)}' -Force|Out-Null;New-ItemProperty -LiteralPath '{Ps(application.Location)}' -Name '{Ps(name)}' -Value $v -PropertyType String -Force|Out-Null", application.RequiresAdmin, cancellationToken: cancellationToken);
            return;
        }
        if (application.RequiresAdmin) await _runner.RunPowerShellAsync($"Move-Item -LiteralPath '{Ps(application.Command)}' -Destination '{Ps(application.Location)}' -Force", true, cancellationToken: cancellationToken);
        else File.Move(application.Command, application.Location, true);
    }

    private async Task<Dictionary<string, StartupBackup>> ReadBackupsAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try { return await ReadBackupsUnsafeAsync(cancellationToken); }
        finally { _gate.Release(); }
    }
    private static async Task<Dictionary<string, StartupBackup>> ReadBackupsUnsafeAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(BackupFile)) return [];
        await using var stream = File.OpenRead(BackupFile);
        return await JsonSerializer.DeserializeAsync<Dictionary<string, StartupBackup>>(stream, cancellationToken: cancellationToken) ?? [];
    }
    private static async Task WriteBackupsUnsafeAsync(Dictionary<string, StartupBackup> backups, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(BackupFile)!);
        var temporary = BackupFile + ".tmp";
        await using (var stream = File.Create(temporary)) await JsonSerializer.SerializeAsync(stream, backups, cancellationToken: cancellationToken);
        File.Move(temporary, BackupFile, true);
    }
    private static StartupApplication ParseStartup(JsonElement item) => new(Get(item, "Id"), Get(item, "Name"), Get(item, "Publisher", "Publisher unavailable"), Get(item, "Impact", "Unknown"), true,
        Get(item, "Source"), Get(item, "Location"), Get(item, "Command"), GetBool(item, "RequiresAdmin"), Get(item, "Kind", "registry"));
    private static string Get(JsonElement item, string name, string fallback = "") => item.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null ? value.ToString() : fallback;
    private static bool GetBool(JsonElement item, string name) => item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
    private static string Ps(string value) => value.Replace("'", "''", StringComparison.Ordinal);
    private sealed record StartupBackup(StartupApplication Application, DateTimeOffset DisabledAt);

    private const string StartupScanScript = """
        $items=@()
        $locations=@(
          @{Path='Registry::HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run';Source='HKCU Run';Admin=$false},
          @{Path='Registry::HKEY_LOCAL_MACHINE\Software\Microsoft\Windows\CurrentVersion\Run';Source='HKLM Run';Admin=$true},
          @{Path='Registry::HKEY_LOCAL_MACHINE\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run';Source='HKLM Run (32-bit)';Admin=$true}
        )
        foreach($location in $locations){if(Test-Path $location.Path){$key=Get-ItemProperty -LiteralPath $location.Path;foreach($property in $key.PSObject.Properties|Where-Object{$_.Name -notmatch '^PS'}){$command=[string]$property.Value;$candidate=if($command -match '^"([^"]+)"'){$matches[1]}else{($command -split ' ')[0]};$publisher=if(Test-Path $candidate){(Get-Item $candidate).VersionInfo.CompanyName}else{$null};$items+=[ordered]@{Id=('reg|'+$location.Path+'|'+$property.Name);Name=$property.Name;Publisher=$publisher;Impact='Unknown';Source=$location.Source;Location=$location.Path;Command=$command;RequiresAdmin=$location.Admin;Kind='registry'}}}}
        $folders=@(@{Path=[Environment]::GetFolderPath('Startup');Source='User Startup Folder';Admin=$false},@{Path=[Environment]::GetFolderPath('CommonStartup');Source='All Users Startup Folder';Admin=$true})
        foreach($folder in $folders){if(Test-Path $folder.Path){Get-ChildItem -LiteralPath $folder.Path -File|ForEach-Object{$items+=[ordered]@{Id=('file|'+$_.FullName);Name=$_.BaseName;Publisher=$null;Impact='Unknown';Source=$folder.Source;Location=$_.FullName;Command=$_.FullName;RequiresAdmin=$folder.Admin;Kind='file'}}}}
        $items|ConvertTo-Json -Depth 4 -Compress
        """;
}

public sealed class GameDetectionService : IGameDetectionService
{
    private readonly ISystemInfoService _systemInfo;
    public GameDetectionService(ISystemInfoService systemInfo) => _systemInfo = systemInfo;
    public async Task<IReadOnlyDictionary<string, string>> DetectAsync(CancellationToken cancellationToken = default) =>
        (await _systemInfo.ScanAsync(cancellationToken)).InstalledGames ?? new Dictionary<string, string>();
}

public sealed class LocalEntitlementService : IEntitlementService
{
    private static string ClaimsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Horizon", "entitlements.json");
    private EntitlementClaims? _cache;
    public async Task<PlanTier> GetPlanAsync(CancellationToken cancellationToken = default)
    {
        var claims = await LoadVerifiedAsync(cancellationToken);
        return claims?.Owned.Contains("ultimate", StringComparer.OrdinalIgnoreCase) == true ? PlanTier.Ultimate
            : claims?.Owned.Contains("performance", StringComparer.OrdinalIgnoreCase) == true ? PlanTier.Performance : PlanTier.Starter;
    }
    public async Task<bool> HasEntitlementAsync(string entitlement, CancellationToken cancellationToken = default)
    {
        var claims = await LoadVerifiedAsync(cancellationToken);
        return claims?.Owned.Contains("ultimate", StringComparer.OrdinalIgnoreCase) == true || claims?.Owned.Contains(entitlement, StringComparer.OrdinalIgnoreCase) == true;
    }
    private async Task<EntitlementClaims?> LoadVerifiedAsync(CancellationToken cancellationToken)
    {
        if (_cache is not null) return _cache;
        var publicKey = Environment.GetEnvironmentVariable("HORIZON_ENTITLEMENT_PUBLIC_KEY");
        if (string.IsNullOrWhiteSpace(publicKey) || !File.Exists(ClaimsPath)) return null;
        await using var stream = File.OpenRead(ClaimsPath);
        var envelope = await JsonSerializer.DeserializeAsync<SignedEntitlementEnvelope>(stream, cancellationToken: cancellationToken);
        if (envelope is null) return null;
        var payload = Convert.FromBase64String(envelope.Payload);
        using var rsa = RSA.Create(); rsa.ImportFromPem(publicKey);
        if (!rsa.VerifyData(payload, Convert.FromBase64String(envelope.Signature), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)) return null;
        var claims = JsonSerializer.Deserialize<EntitlementClaims>(payload);
        if (claims is null || claims.ValidUntil is not null && claims.ValidUntil <= DateTimeOffset.UtcNow) return null;
        return _cache = claims;
    }
    private sealed record SignedEntitlementEnvelope(string Payload, string Signature);
    private sealed record EntitlementClaims(string AccountId, string[] Owned, DateTimeOffset? ValidUntil);
}

public sealed class PurchaseService : IPurchaseService
{
    public Task BeginPurchaseAsync(PlanTier plan, CancellationToken cancellationToken = default) => BeginPurchaseAsync(plan.ToString().ToLowerInvariant(), cancellationToken);
    public Task BeginPurchaseAsync(string productId, CancellationToken cancellationToken = default)
    {
        var baseUrl = Environment.GetEnvironmentVariable("HORIZON_PURCHASE_URL");
        if (string.IsNullOrWhiteSpace(baseUrl)) throw new InvalidOperationException("Horizon checkout is not configured.");
        if (!Regex.IsMatch(productId, "^[a-zA-Z0-9-]+$")) throw new InvalidOperationException("The product identifier is invalid.");
        var separator = baseUrl.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        Process.Start(new ProcessStartInfo($"{baseUrl}{separator}product={Uri.EscapeDataString(productId)}") { UseShellExecute = true });
        return Task.CompletedTask;
    }
}

public sealed class BenchmarkService : IBenchmarkService
{
    public Task<BenchmarkResult> RunAsync(IProgress<double>? progress = null, CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        const int batches = 20;
        const int perBatch = 25_000;
        var buffer = new byte[1024];
        RandomNumberGenerator.Fill(buffer);
        long operations = 0;
        var watch = Stopwatch.StartNew();
        for (var batch = 0; batch < batches; batch++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var index = 0; index < perBatch; index++) { buffer = SHA256.HashData(buffer); operations++; }
            progress?.Report((batch + 1d) / batches);
        }
        watch.Stop();
        return new BenchmarkResult(DateTimeOffset.UtcNow, watch.Elapsed.TotalSeconds, operations, operations / watch.Elapsed.TotalSeconds,
            Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "Detected processor", Environment.ProcessorCount);
    }, cancellationToken);
}

public sealed class UpdateService : IUpdateService
{
    private readonly HttpClient _http;

    public UpdateService() : this(new HttpClient()) { }

    internal UpdateService(HttpClient http)
    {
        _http = http;
        _http.Timeout = TimeSpan.FromSeconds(8);
        if (!_http.DefaultRequestHeaders.UserAgent.Any())
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("Horizon-Windows/0.1");
    }

    public async Task<string?> CheckAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync("https://api.github.com/repos/lachlan1234564/Horzion-Labs/releases/latest", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("tag_name", out var tagElement)) return null;
        var tag = tagElement.GetString()?.Trim();
        if (string.IsNullOrWhiteSpace(tag)) return null;

        var normalized = tag.TrimStart('v', 'V').Split('-', 2)[0];
        if (!Version.TryParse(normalized, out var available)) return null;
        var current = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 1, 0);
        return available > current ? tag : null;
    }
}

public sealed class ToastService : IToastService
{
    public event EventHandler<ToastMessage>? ToastRequested;
    public void Show(string message, StatusTone tone = StatusTone.Neutral, TimeSpan? duration = null) => ToastRequested?.Invoke(this, new(Guid.NewGuid(), message, tone, duration ?? TimeSpan.FromSeconds(5)));
}

public sealed class DialogService : IDialogService
{
    private readonly Dictionary<Guid, TaskCompletionSource<bool>> _pending = [];
    private readonly object _gate = new();
    public event EventHandler<DialogRequest>? DialogRequested;

    public Task<bool> ShowAsync(DialogKind kind, string title, string message, string primaryAction = "CONTINUE", string secondaryAction = "CANCEL", StatusTone tone = StatusTone.Neutral)
    {
        var id = Guid.NewGuid();
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate) _pending[id] = completion;
        DialogRequested?.Invoke(this, new(id, kind, title, message, primaryAction, secondaryAction, tone));
        return completion.Task;
    }

    public void Complete(Guid id, bool accepted)
    {
        TaskCompletionSource<bool>? completion;
        lock (_gate)
        {
            if (!_pending.Remove(id, out completion)) return;
        }
        completion.TrySetResult(accepted);
    }
}

public static class ExternalLinks
{
    public const string DiscordSupport = "https://discord.gg/KbcHSEDNR";
    public static void Open(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed) || parsed.Scheme is not ("https" or "http")) throw new InvalidOperationException("Unsupported link.");
        Process.Start(new ProcessStartInfo(parsed.AbsoluteUri) { UseShellExecute = true });
    }
}
