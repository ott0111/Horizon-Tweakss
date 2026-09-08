namespace Horizon.Core.Models;

public enum NavigationDestination
{
    Dashboard,
    Recommended,
    AllTweaks,
    Fortnite,
    Debloat,
    Startup,
    Cleanup,
    System,
    Compatibility,
    Benchmark,
    Restore,
    History,
    Profile,
    Plan,
    Settings,
    Support
}

public enum StatusTone { Neutral, Success, Warning, Danger }
public enum TweakRisk { Low, Medium, Advanced }
public enum RestartRequirement { None, Application, Explorer, NetworkAdapter, SignOut, Windows }
public enum TweakApplyState { Available, Applied, AlreadyApplied, Restored, Unsupported, AdminRequired, RestartRequired, UpdateChanged, Locked, Failed, Skipped }
public enum CleanupState { Scanning, Ready, Cleaning, Complete, Error }
public enum BenchmarkState { NoData, Ready, Running, Complete, Failed }
public enum ThemePreference { Dark, Light, System }
public enum PlanTier { Starter, Performance, Ultimate }
public enum TweakChangeResult { Applied, AlreadyApplied, Restored, Skipped, Failed }
public enum DialogKind { AdministratorAccess, GeneralError, TweakFailed, Unsupported, RestartRequired, RestoreConfirmation, DeleteHistory, SignOut, UpdateRequired, NoInternet, BackendOffline, AccountError, PurchaseError }
public enum AuthenticationState { Idle, Validating, OpeningProvider, WaitingForCallback, SigningIn, Success, Error, Unavailable, Cancelled }

public sealed record AuthenticationProviderAvailability(string Name, bool IsConfigured, string Message);
public sealed record AuthenticationStateChanged(AuthenticationState State, string Message);
public sealed record AuthenticationResult(UserProfile Profile, bool IsNewAccount);
public sealed record PasswordResetRequestResult(bool Accepted, string? DevelopmentToken = null);
public sealed record StoredAuthenticationSession(string AccessToken, string RefreshToken, DateTimeOffset AccessExpiresAt, string ApiBaseUrl);

public sealed class HorizonAuthenticationException : Exception
{
    public HorizonAuthenticationException(string message, AuthenticationState state = AuthenticationState.Error, Exception? innerException = null)
        : base(message, innerException) => State = state;

    public AuthenticationState State { get; }
}

public sealed record TweakDefinition(
    string Id,
    string Name,
    string Category,
    string Description,
    string WhatItDoes,
    TweakRisk Risk = TweakRisk.Low,
    bool Recommended = false,
    bool RequiresAdmin = false,
    RestartRequirement Restart = RestartRequirement.None,
    bool Temporary = false,
    bool HardwareSpecific = false,
    PlanTier RequiredPlan = PlanTier.Starter,
    string? RequiredEntitlement = null,
    string Compatibility = "windows",
    bool Actionable = true);

public sealed record TweakStatus(
    string TweakId,
    bool Compatible,
    TweakApplyState State,
    string CurrentValue,
    string HorizonValue,
    string? CompatibilityReason = null,
    bool Verified = false,
    bool HasActiveBackup = false,
    string? Error = null);

public sealed record TweakListItem(TweakDefinition Definition, TweakStatus Status);

public sealed record TweakFilter(
    string Search = "",
    TweakRisk? Risk = null,
    TweakApplyState? State = null,
    bool RecommendedOnly = false,
    bool AppliedOnly = false,
    bool NotAppliedOnly = false)
{
    public int ActiveCount =>
        (Risk is null ? 0 : 1) + (State is null ? 0 : 1) +
        (RecommendedOnly ? 1 : 0) + (AppliedOnly ? 1 : 0) + (NotAppliedOnly ? 1 : 0);
}

public sealed record HardwareValue(string Label, string Value, string Icon);

public sealed record GpuSnapshot(string Name, string Vendor, string Driver, long VramBytes);
public sealed record MemoryModuleSnapshot(long CapacityBytes, int Speed, int ConfiguredSpeed, string Manufacturer, string PartNumber)
{
    public string Display => $"{SizeFormatter.Format(CapacityBytes)} · {(ConfiguredSpeed > 0 ? ConfiguredSpeed : Speed)} MT/s · {Manufacturer}";
}
public sealed record DriveSnapshot(string Name, string MediaType, string BusType, string Health, long SizeBytes, string? Volume, long FreeBytes)
{
    public string DisplaySize => SizeFormatter.Format(SizeBytes);
    public string DisplayFree => FreeBytes > 0 ? $"{SizeFormatter.Format(FreeBytes)} free" : "—";
}
public sealed record NetworkAdapterSnapshot(string Name, string Description, string InterfaceGuid, string LinkSpeed, string Driver, string MediaType);
public sealed record InputDeviceSnapshot(string Name, string Class, string InstanceId, string Status);

public sealed record SystemSnapshot(
    string DeviceName,
    string WindowsEdition,
    string WindowsBuild,
    string Cpu,
    int CpuCores,
    int CpuThreads,
    string Gpu,
    string GpuDriver,
    string Vram,
    string Ram,
    string MemorySpeed,
    string Storage,
    string StorageFree,
    string NetworkAdapter,
    string Motherboard,
    string Bios,
    bool FortniteInstalled,
    string WindowsVersion = "—",
    bool IsAdministrator = false,
    IReadOnlyList<GpuSnapshot>? Gpus = null,
    IReadOnlyList<MemoryModuleSnapshot>? MemoryModules = null,
    IReadOnlyList<DriveSnapshot>? Drives = null,
    IReadOnlyList<NetworkAdapterSnapshot>? NetworkAdapters = null,
    IReadOnlyList<InputDeviceSnapshot>? InputDevices = null,
    IReadOnlyDictionary<string, string>? InstalledGames = null,
    IReadOnlyDictionary<string, string>? DetectedSoftware = null,
    DateTimeOffset? DetectedAt = null,
    bool PlatformSupported = true)
{
    public static SystemSnapshot Unknown { get; } = new(
        Environment.MachineName, "Detecting Windows…", "—", "Detecting CPU…", 0, 0,
        "Detecting GPU…", "—", "—", "Detecting memory…", "—", "Detecting storage…",
        "—", "Detecting network…", "—", "—", false);
}

public sealed class CleanupCategory
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string Icon { get; init; }
    public long DetectedBytes { get; set; }
    public bool IsSelected { get; set; } = true;
    public string DisplaySize => SizeFormatter.Format(DetectedBytes);
}

public sealed record StartupApplication(string Id, string Name, string Publisher, string Impact, bool Enabled, string Source,
    string Location = "", string Command = "", bool RequiresAdmin = false, string Kind = "registry", string? BackupId = null);
public sealed record InstalledPackage(string Id, string Name, string Publisher, bool Recommended, bool Selected = false, bool Removable = true, string Group = "Optional");
public sealed record GameProfile(string Id, string Name, string Description, bool IsActive = false);

public sealed record TweakValue(bool Exists, string? Value, string ValueType = "String", string? Source = null, string? BackupPath = null)
{
    public static TweakValue Missing(string? source = null) => new(false, null, Source: source);
    public string Display => Exists ? Value ?? "(empty)" : "Not configured";
}

public sealed record OperationCompatibility(bool Supported, string? Reason = null);

public sealed record ActiveTweakChange(
    string TweakId,
    Guid SessionId,
    string Source,
    TweakValue Previous,
    TweakValue Applied,
    DateTimeOffset AppliedAt,
    bool Temporary,
    RestartRequirement Restart);

public sealed record OptimizationChange(
    string TweakId,
    string Name,
    string PreviousValue,
    string NewValue,
    bool Success,
    string? Error = null,
    TweakChangeResult Result = TweakChangeResult.Applied,
    bool Verified = false,
    RestartRequirement Restart = RestartRequirement.None);

public sealed record OptimizationSession(Guid Id, DateTimeOffset StartedAt, string Name, IReadOnlyList<OptimizationChange> Changes, bool Success,
    DateTimeOffset? CompletedAt = null, RestartRequirement Restart = RestartRequirement.None, bool RestorePointCreated = false, string Source = "user")
{
    public int ChangeCount => Changes.Count;
}

public sealed record RestoreSnapshot(Guid Id, DateTimeOffset CreatedAt, Guid SessionId, IReadOnlyList<OptimizationChange> Changes);

public sealed record CommandResult(int ExitCode, string StandardOutput, string StandardError);
public sealed record GamingModeStatus(bool Enabled, string? ActiveGame, Guid? ActiveSessionId, DateTimeOffset? StartedAt, DateTimeOffset? LastRestoredAt, string? Error = null);
public sealed record BenchmarkResult(
    DateTimeOffset CapturedAt,
    double DurationSeconds,
    long Operations,
    double OperationsPerSecond,
    string Processor,
    int LogicalProcessors,
    double? AverageFps = null,
    double? OnePercentLowFps = null,
    double? FrameTimeMs = null,
    string Phase = "Baseline");

public sealed record DialogRequest(Guid Id, DialogKind Kind, string Title, string Message, string PrimaryAction, string SecondaryAction, StatusTone Tone);

public sealed class AppSettings
{
    public bool LaunchWithWindows { get; set; }
    public bool MinimizeToTray { get; set; } = true;
    public string CloseBehavior { get; set; } = "Minimize to tray";
    public string Language { get; set; } = "English";
    public bool AutomaticScan { get; set; } = true;
    public bool RecordPreviousValues { get; set; } = true;
    public bool CreateRestorePoint { get; set; } = true;
    public bool VerifyChanges { get; set; } = true;
    public bool AskBeforeRestarting { get; set; } = true;
    public bool RescanAfterWindowsUpdate { get; set; } = true;
    public bool DetectSupportedGames { get; set; } = true;
    public bool AutomaticTemporaryProfile { get; set; }
    public bool RestoreTemporarySettings { get; set; } = true;
    public bool GamingNotifications { get; set; } = true;
    public bool NotifyOptimizationComplete { get; set; } = true;
    public bool NotifyGameProfileActivated { get; set; } = true;
    public bool NotifyRestartRequired { get; set; } = true;
    public bool NotifyUpdateAvailable { get; set; } = true;
    public ThemePreference Theme { get; set; } = ThemePreference.Dark;
    public bool AutomaticUpdates { get; set; } = true;
    public bool Diagnostics { get; set; }
    public bool HardwareInformation { get; set; } = true;
    public bool SupportLogs { get; set; } = true;
    public bool AdvancedTweaks { get; set; }
    public bool ExperimentalFeatures { get; set; }
    public bool OnboardingCompleted { get; set; }
}

public sealed class UserProfile
{
    public string? AccountId { get; set; }
    public string DisplayName { get; set; } = "Lachlan";
    public string? Email { get; set; }
    public bool EmailVerified { get; set; }
    public List<string> AuthenticationProviders { get; set; } = [];
    public DateTimeOffset? AccountCreatedAt { get; set; }
    public int OnboardingStep { get; set; }
    public bool OnboardingCompleted { get; set; }
    public string DeviceName { get; set; } = Environment.MachineName;
    public string? AvatarPath { get; set; }
    public string? ProviderAvatarUrl { get; set; }
    public PlanTier Plan { get; set; } = PlanTier.Starter;
    public string Initials => string.IsNullOrWhiteSpace(DisplayName) ? "L" : string.Concat(DisplayName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(x => x[0])).ToUpperInvariant();
}

public static class SizeFormatter
{
    public static string Format(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = Math.Max(0, bytes);
        var unit = 0;
        var size = (double)value;
        while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
        return unit == 0 ? $"{size:0} {units[unit]}" : $"{size:0.#} {units[unit]}";
    }
}
