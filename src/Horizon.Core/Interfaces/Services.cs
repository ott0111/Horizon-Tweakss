using Horizon.Core.Models;

namespace Horizon.Core.Interfaces;

public interface IAuthenticationService
{
    event EventHandler<AuthenticationStateChanged>? StateChanged;
    IReadOnlyList<AuthenticationProviderAvailability> Providers { get; }
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<AuthenticationResult?> TryRestoreSessionAsync(CancellationToken cancellationToken = default);
    Task<AuthenticationResult> SignInAsync(string email, string password, CancellationToken cancellationToken = default);
    Task<AuthenticationResult> SignInWithProviderAsync(string provider, CancellationToken cancellationToken = default);
    Task<AuthenticationResult> LinkProviderAsync(string provider, CancellationToken cancellationToken = default);
    Task<AuthenticationResult> CreateAccountAsync(string displayName, string email, string password, CancellationToken cancellationToken = default);
    Task<PasswordResetRequestResult> RequestPasswordResetAsync(string email, CancellationToken cancellationToken = default);
    Task ResetPasswordAsync(string token, string password, CancellationToken cancellationToken = default);
    Task VerifyEmailAsync(string token, CancellationToken cancellationToken = default);
    Task<UserProfile> UpdateProfileAsync(string displayName, string? profilePictureUrl, CancellationToken cancellationToken = default);
    Task<UserProfile> UpdateOnboardingAsync(int step, bool completed, CancellationToken cancellationToken = default);
    Task SignOutAsync(CancellationToken cancellationToken = default);
}

public interface ISecureSessionStore
{
    Task<StoredAuthenticationSession?> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(StoredAuthenticationSession session, CancellationToken cancellationToken = default);
    Task ClearAsync(CancellationToken cancellationToken = default);
}

public interface IAppLogger
{
    string LogDirectory { get; }
    void Info(string operation, string message, string? page = null);
    void Error(string operation, Exception exception, string? page = null, string? message = null);
}

public interface ISettingsService
{
    Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
    Task<UserProfile> LoadProfileAsync(CancellationToken cancellationToken = default);
    Task SaveProfileAsync(UserProfile profile, CancellationToken cancellationToken = default);
    Task ResetAsync(CancellationToken cancellationToken = default);
}

public interface ISystemInfoService
{
    Task<SystemSnapshot> ScanAsync(CancellationToken cancellationToken = default);
    Task<SystemSnapshot> RefreshAsync(CancellationToken cancellationToken = default);
}
public interface ITweakCatalog { IReadOnlyList<TweakDefinition> GetAll(); }
public interface ICompatibilityScanner { Task<IReadOnlyDictionary<string, TweakStatus>> ScanAsync(IEnumerable<TweakDefinition> tweaks, CancellationToken cancellationToken = default); }

public interface ITweakEngine
{
    Task<IReadOnlyList<TweakListItem>> ScanAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TweakListItem>> RefreshScanAsync(CancellationToken cancellationToken = default);
    Task<OptimizationSession> ApplyAsync(IEnumerable<string> tweakIds, CancellationToken cancellationToken = default);
    Task<OptimizationSession> ApplySessionAsync(IEnumerable<string> tweakIds, string name, string source, CancellationToken cancellationToken = default);
    Task<OptimizationSession> RestoreAsync(IEnumerable<string> tweakIds, CancellationToken cancellationToken = default);
    Task<OptimizationSession> RestoreSessionAsync(Guid sessionId, CancellationToken cancellationToken = default);
}

public interface ICommandRunner
{
    bool IsWindows { get; }
    Task<CommandResult> RunPowerShellAsync(string script, bool elevated = false, TimeSpan? timeout = null, CancellationToken cancellationToken = default);
}

public interface ITweakOperation
{
    string TweakId { get; }
    string DesiredDisplay { get; }
    Task<OperationCompatibility> CheckCompatibilityAsync(SystemSnapshot system, CancellationToken cancellationToken = default);
    Task<TweakValue> ReadAsync(SystemSnapshot system, CancellationToken cancellationToken = default);
    Task<TweakValue> BackupAsync(TweakValue current, Guid sessionId, CancellationToken cancellationToken = default);
    Task ApplyAsync(SystemSnapshot system, CancellationToken cancellationToken = default);
    bool IsDesired(TweakValue current);
    Task RestoreAsync(TweakValue previous, SystemSnapshot system, CancellationToken cancellationToken = default);
    bool IsRestored(TweakValue current, TweakValue previous);
}

public interface ITweakOperationRegistry
{
    bool TryGet(string tweakId, out ITweakOperation operation);
}

public interface IActiveChangeStore
{
    Task<IReadOnlyDictionary<string, ActiveTweakChange>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<ActiveTweakChange?> GetAsync(string tweakId, CancellationToken cancellationToken = default);
    Task UpsertAsync(ActiveTweakChange change, CancellationToken cancellationToken = default);
    Task RemoveAsync(string tweakId, CancellationToken cancellationToken = default);
}

public interface IHistoryService
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task AddAsync(OptimizationSession session, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OptimizationSession>> GetRecentAsync(int limit = 50, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task ClearAsync(CancellationToken cancellationToken = default);
}

public interface IRestoreService
{
    Task<OptimizationSession?> UndoLastAsync(CancellationToken cancellationToken = default);
    Task<OptimizationSession> RestoreSessionAsync(Guid sessionId, CancellationToken cancellationToken = default);
    Task<OptimizationSession> RestoreAllAsync(CancellationToken cancellationToken = default);
}

public interface ICleanupService
{
    Task<IReadOnlyList<CleanupCategory>> ScanAsync(IProgress<double>? progress = null, CancellationToken cancellationToken = default);
    Task<long> CleanAsync(IEnumerable<CleanupCategory> categories, IProgress<double>? progress = null, CancellationToken cancellationToken = default);
}

public interface IDebloatService
{
    Task<IReadOnlyList<InstalledPackage>> GetInstalledPackagesAsync(CancellationToken cancellationToken = default);
    Task RemoveAsync(IEnumerable<string> packageIds, CancellationToken cancellationToken = default);
}

public interface IStartupService
{
    Task<IReadOnlyList<StartupApplication>> GetApplicationsAsync(CancellationToken cancellationToken = default);
    Task SetEnabledAsync(string id, bool enabled, CancellationToken cancellationToken = default);
}

public interface IGameDetectionService { Task<IReadOnlyDictionary<string, string>> DetectAsync(CancellationToken cancellationToken = default); }
public interface IEntitlementService { Task<PlanTier> GetPlanAsync(CancellationToken cancellationToken = default); Task<bool> HasEntitlementAsync(string entitlement, CancellationToken cancellationToken = default); }
public interface IPurchaseService { Task BeginPurchaseAsync(PlanTier plan, CancellationToken cancellationToken = default); Task BeginPurchaseAsync(string productId, CancellationToken cancellationToken = default); }
public interface IUpdateService { Task<string?> CheckAsync(CancellationToken cancellationToken = default); }
public interface ILaunchAtStartupService
{
    Task<bool> GetEnabledAsync(CancellationToken cancellationToken = default);
    Task SetEnabledAsync(bool enabled, CancellationToken cancellationToken = default);
}
public interface IBenchmarkService { Task<BenchmarkResult> RunAsync(IProgress<double>? progress = null, CancellationToken cancellationToken = default); }

public interface IGamingModeService : IAsyncDisposable
{
    event EventHandler<GamingModeStatus>? StatusChanged;
    Task<GamingModeStatus> GetStatusAsync(CancellationToken cancellationToken = default);
    Task ConfigureAsync(string gameId, bool enabled, IEnumerable<string> temporaryTweakIds, CancellationToken cancellationToken = default);
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}

public interface IToastService
{
    event EventHandler<ToastMessage>? ToastRequested;
    void Show(string message, StatusTone tone = StatusTone.Neutral, TimeSpan? duration = null);
}

public interface IDialogService
{
    event EventHandler<DialogRequest>? DialogRequested;
    Task<bool> ShowAsync(DialogKind kind, string title, string message, string primaryAction = "CONTINUE", string secondaryAction = "CANCEL", StatusTone tone = StatusTone.Neutral);
    void Complete(Guid id, bool accepted);
}

public sealed record ToastMessage(Guid Id, string Message, StatusTone Tone, TimeSpan Duration);
