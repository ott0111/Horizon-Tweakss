using Horizon.Core.Interfaces;
using Horizon.Core.Models;

namespace Horizon.App.ViewModels;

public sealed class RootViewModel : ObservableObject
{
    private readonly IAuthenticationService _authentication;
    private readonly ISettingsService _settingsService;
    private readonly IEntitlementService _entitlements;
    private readonly IAppLogger _logger;
    private readonly ISystemInfoService _systemInfo;
    private readonly ITweakEngine _tweakEngine;
    private readonly ICommandRunner _commandRunner;
    private readonly Func<ShellViewModel> _shellFactory;
    private object? _current;
    private AppSettings _settings = new();
    private UserProfile _profile = new();

    public RootViewModel(IAuthenticationService authentication, ISettingsService settingsService, IEntitlementService entitlements,
        IAppLogger logger, ISystemInfoService systemInfo, ITweakEngine tweakEngine, ICommandRunner commandRunner, Func<ShellViewModel> shellFactory)
    {
        _authentication = authentication;
        _settingsService = settingsService;
        _entitlements = entitlements;
        _logger = logger;
        _systemInfo = systemInfo;
        _tweakEngine = tweakEngine;
        _commandRunner = commandRunner;
        _shellFactory = shellFactory;
        ShowSignIn();
    }

    public object? Current
    {
        get => _current;
        private set
        {
            if (ReferenceEquals(_current, value)) return;
            var previous = _current;
            if (Set(ref _current, value)) (previous as IDisposable)?.Dispose();
        }
    }
    public AppSettings Settings => _settings;
    public UserProfile Profile => _profile;
    public string CurrentPageName => Current is ShellViewModel shell ? shell.SelectedDestination.ToString() : Current?.GetType().Name ?? "Startup";

    public async Task InitializeAsync()
    {
        var initialAuthenticationView = Current;
        _settings = await _settingsService.LoadAsync();
        _profile = await _settingsService.LoadProfileAsync();
        _logger.Info("startup.profile", "Local settings and profile loaded.", CurrentPageName);
        await _authentication.InitializeAsync();
        if (!ReferenceEquals(Current, initialAuthenticationView)) return;
        try
        {
            var restored = await _authentication.TryRestoreSessionAsync();
            if (restored is not null && ReferenceEquals(Current, initialAuthenticationView))
                await OnAuthenticated(restored, initialAuthenticationView);
        }
        catch (HorizonAuthenticationException exception)
        {
            _logger.Error("auth.restore", exception, CurrentPageName, "Stored session could not be refreshed; sign-in remains available.");
        }
    }

    private async Task OnAuthenticated(AuthenticationResult result, object? expectedCurrent = null)
    {
        var profile = result.Profile;
        profile.Plan = await _entitlements.GetPlanAsync();
        if (expectedCurrent is not null && !ReferenceEquals(Current, expectedCurrent)) return;
        _profile = profile;
        await _settingsService.SaveProfileAsync(profile);
        if (expectedCurrent is not null && !ReferenceEquals(Current, expectedCurrent)) return;
        if (result.IsNewAccount || !profile.OnboardingCompleted)
        {
            var onboarding = new OnboardingViewModel(profile, _settings, _authentication, _settingsService, _systemInfo,
                _commandRunner, _logger, UpdateOnboarding, FinishOnboardingLaterAsync);
            Current = onboarding;
            await onboarding.InitializeAsync();
            return;
        }
        await ShowShellAsync();
    }

    private async Task UpdateOnboarding(int step, bool completed)
    {
        var localProfile = await _settingsService.LoadProfileAsync();
        var localAvatar = string.Equals(localProfile.AccountId, _profile.AccountId, StringComparison.Ordinal) ? localProfile.AvatarPath : null;
        _profile = await _authentication.UpdateOnboardingAsync(step, completed);
        _profile.AvatarPath = localAvatar;
        _profile.Plan = await _entitlements.GetPlanAsync();
        _settings.OnboardingCompleted = completed;
        await _settingsService.SaveAsync(_settings);
        await _settingsService.SaveProfileAsync(_profile);
        if (completed) await ShowShellAsync();
    }

    private async Task FinishOnboardingLaterAsync(int step)
    {
        await UpdateOnboarding(step, false);
        await ShowShellAsync();
    }

    private async Task ShowShellAsync()
    {
        var shell = _shellFactory();
        shell.SetProfile(_profile);
        shell.SetSignOut(SignOutAsync);
        shell.SetSettingsUpdated(settings => _settings = settings);
        Current = shell;
        await shell.InitializeAsync();
    }

    private async Task SignOutAsync()
    {
        await _authentication.SignOutAsync();
        ShowSignIn();
    }

    private void ShowSignIn() => Current = new SignInViewModel(_authentication, _logger, result => OnAuthenticated(result), ShowSignUp, ShowForgotPassword);
    private void ShowSignUp() => Current = new SignUpViewModel(_authentication, _logger, result => OnAuthenticated(result), ShowSignIn);
    private void ShowForgotPassword(string email) => Current = new ForgotPasswordViewModel(_authentication, _logger, email, ShowSignIn, ShowResetPassword);
    private void ShowResetPassword(string email, string? token) => Current = new ResetPasswordViewModel(_authentication, _logger, email, token, ShowSignIn);
}

public sealed class SignInViewModel : ObservableObject, IDisposable
{
    private readonly IAuthenticationService _authentication;
    private readonly IAppLogger _logger;
    private readonly Func<AuthenticationResult, Task> _authenticated;
    private string _email = string.Empty;
    private string _password = string.Empty;
    private string _error = string.Empty;
    private string _statusTitle = string.Empty;
    private string _statusMessage = string.Empty;
    private AuthenticationState _state = AuthenticationState.Idle;
    private bool _isBusy;
    private bool _disposed;

    public SignInViewModel(IAuthenticationService authentication, IAppLogger logger, Func<AuthenticationResult, Task> authenticated, Action createAccount, Action<string> forgotPassword)
    {
        _authentication = authentication;
        _logger = logger;
        _authenticated = authenticated;
        _authentication.StateChanged += OnAuthenticationStateChanged;
        SignInCommand = new AsyncRelayCommand(SignInAsync, () => CanSignIn);
        ProviderCommand = new AsyncRelayCommand(SignInWithProviderAsync, _ => !IsBusy);
        ResetPasswordCommand = new RelayCommand(() => forgotPassword(Email), () => !IsBusy);
        CreateAccountCommand = new RelayCommand(createAccount, () => !IsBusy);
    }

    public string Email
    {
        get => _email;
        set
        {
            if (!Set(ref _email, value)) return;
            Raise(nameof(IsEmailEmpty));
            Raise(nameof(CanSignIn));
            SignInCommand.NotifyCanExecuteChanged();
        }
    }
    public string Password
    {
        get => _password;
        set
        {
            if (!Set(ref _password, value)) return;
            Raise(nameof(CanSignIn));
            SignInCommand.NotifyCanExecuteChanged();
        }
    }
    public string Error { get => _error; private set => Set(ref _error, value); }
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!Set(ref _isBusy, value)) return;
            Raise(nameof(CanSignIn));
            Raise(nameof(PrimaryActionLabel));
            SignInCommand.NotifyCanExecuteChanged();
            ProviderCommand.NotifyCanExecuteChanged();
            ResetPasswordCommand.NotifyCanExecuteChanged();
            CreateAccountCommand.NotifyCanExecuteChanged();
        }
    }
    public AuthenticationState State { get => _state; private set { if (Set(ref _state, value)) { Raise(nameof(IsStatusVisible)); Raise(nameof(StatusTone)); Raise(nameof(PrimaryActionLabel)); } } }
    public string StatusTitle { get => _statusTitle; private set => Set(ref _statusTitle, value); }
    public string StatusMessage { get => _statusMessage; private set => Set(ref _statusMessage, value); }
    public bool IsStatusVisible => State is not AuthenticationState.Idle and not AuthenticationState.Success;
    public bool IsEmailEmpty => string.IsNullOrWhiteSpace(Email);
    public bool CanSignIn => !IsBusy && !string.IsNullOrWhiteSpace(Email) && !string.IsNullOrWhiteSpace(Password);
    public string PrimaryActionLabel => State == AuthenticationState.SigningIn && IsBusy ? "SIGNING IN…" : "SIGN IN";
    public StatusTone StatusTone => State switch
    {
        AuthenticationState.Error => StatusTone.Danger,
        AuthenticationState.Unavailable or AuthenticationState.Cancelled => StatusTone.Warning,
        _ => StatusTone.Neutral
    };
    public string DiscordToolTip => ProviderToolTip("Discord");
    public string GoogleToolTip => ProviderToolTip("Google");
    public string EpicToolTip => ProviderToolTip("Epic Games");
    public AsyncRelayCommand SignInCommand { get; }
    public AsyncRelayCommand ProviderCommand { get; }
    public RelayCommand ResetPasswordCommand { get; }
    public RelayCommand CreateAccountCommand { get; }

    private async Task SignInAsync()
    {
        SetState(AuthenticationState.Validating, "Checking your details", "Validating your email and password.");
        await RunAsync(AuthenticationState.SigningIn, "auth.email", async () =>
        {
            var profile = await _authentication.SignInAsync(Email.Trim(), Password);
            SetState(AuthenticationState.Success, string.Empty, string.Empty);
            await _authenticated(profile);
        });
    }

    private async Task SignInWithProviderAsync(object? provider)
    {
        var name = provider?.ToString() ?? "Discord";
        await RunAsync(AuthenticationState.OpeningProvider, $"auth.provider.{name.ToLowerInvariant().Replace(' ', '-')}", async () =>
        {
            var profile = await _authentication.SignInWithProviderAsync(name);
            await _authenticated(profile);
        }, name);
    }

    private async Task RunAsync(AuthenticationState startingState, string operation, Func<Task> action, string? provider = null)
    {
        IsBusy = true;
        Error = string.Empty;
        SetState(startingState,
            provider is null ? "Signing in…" : $"Opening {provider}…",
            provider is null ? "Connecting securely to Horizon." : "Your browser will open when this provider is available.");
        try
        {
            await action();
            _logger.Info(operation, "Authentication operation completed.", nameof(SignInViewModel));
        }
        catch (OperationCanceledException)
        {
            SetState(AuthenticationState.Cancelled, "Sign-in cancelled", "No account changes were made. You can try again whenever you're ready.");
            _logger.Info(operation, "Authentication operation was cancelled.", nameof(SignInViewModel));
        }
        catch (HorizonAuthenticationException ex)
        {
            var title = provider is null ? "Unable to sign in" : $"{provider} sign-in unavailable";
            SetState(ex.State, title, ex.Message);
            Error = ex.Message;
            _logger.Error(operation, ex, nameof(SignInViewModel), "Controlled authentication failure was displayed in the sign-in screen.");
        }
        catch (ArgumentException ex)
        {
            SetState(AuthenticationState.Error, "Check your details", ex.Message);
            Error = ex.Message;
            _logger.Info(operation, ex.Message, nameof(SignInViewModel));
        }
        catch (Exception ex)
        {
            const string message = "Horizon couldn't complete sign-in. Please try again.";
            SetState(AuthenticationState.Error, "Unable to sign in", message);
            Error = message;
            _logger.Error(operation, ex, nameof(SignInViewModel), "Unexpected authentication failure was contained by the sign-in screen.");
        }
        finally { IsBusy = false; }
    }

    private void SetState(AuthenticationState state, string title, string message)
    {
        State = state;
        StatusTitle = title;
        StatusMessage = message;
    }

    private void OnAuthenticationStateChanged(object? sender, AuthenticationStateChanged change)
    {
        if (_disposed) return;
        var title = change.State switch
        {
            AuthenticationState.OpeningProvider => "Opening your browser…",
            AuthenticationState.WaitingForCallback => "Waiting for provider",
            AuthenticationState.SigningIn => "Signing in…",
            _ => "Authentication"
        };
        SetState(change.State, title, change.Message);
    }

    private string ProviderToolTip(string provider)
    {
        var availability = _authentication.Providers.FirstOrDefault(item => item.Name.Equals(provider, StringComparison.OrdinalIgnoreCase));
        return availability is null ? provider : $"{provider} — {availability.Message}";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _authentication.StateChanged -= OnAuthenticationStateChanged;
    }
}

public sealed class OnboardingViewModel : ObservableObject
{
    private readonly IAuthenticationService _authentication;
    private readonly ISettingsService _settingsService;
    private readonly ISystemInfoService _systemInfo;
    private readonly ICommandRunner _commandRunner;
    private readonly IAppLogger _logger;
    private readonly Func<int, bool, Task> _update;
    private readonly Func<int, Task> _finishLater;
    private readonly AppSettings _settings;
    private UserProfile _profile;
    private SystemSnapshot _snapshot = SystemSnapshot.Unknown;
    private string? _availableProviderPhoto;
    private string _displayName;
    private string _profileStatus = string.Empty;
    private string _scanStatus = "Ready to scan";
    private string _protectionStatus = "Not set up yet";
    private StatusTone _protectionTone = StatusTone.Neutral;
    private int _step;
    private bool _scanComplete;
    private bool _protectionReady;
    private bool _restorePointCreated;
    private bool _isBusy;

    private static readonly (string Eyebrow, string Title, string Description, string Icon)[] Steps =
    [
        ("WELCOME TO HORIZON", "YOUR PC, READY FOR MORE.", "A quick setup will tailor Horizon to this PC.", "bolt"),
        ("YOUR PROFILE", "Make Horizon yours", "Choose how you’ll appear in the app.", "person"),
        ("SCAN YOUR PC", "Let’s see what you’re running", "Horizon checks your hardware, Windows and supported games.", "memory"),
        ("PROTECTION", "Keep every change reversible", "Horizon records supported settings before changing them, so you can restore them later.", "verified_user"),
        ("GAMING", "Set up gaming mode", "Choose how Horizon should respond when a supported game starts.", "sports_esports"),
        ("READY", "YOU’RE READY TO GO.", "Horizon is set up for this PC.", "check_circle")
    ];

    public OnboardingViewModel(UserProfile profile, AppSettings settings, IAuthenticationService authentication,
        ISettingsService settingsService, ISystemInfoService systemInfo,
        ICommandRunner commandRunner, IAppLogger logger, Func<int, bool, Task> update, Func<int, Task> finishLater)
    {
        _profile = profile; _settings = settings; _authentication = authentication; _settingsService = settingsService;
        _systemInfo = systemInfo; _commandRunner = commandRunner; _logger = logger; _update = update; _finishLater = finishLater;
        _displayName = string.IsNullOrWhiteSpace(profile.DisplayName) ? "Lachlan" : profile.DisplayName;
        _availableProviderPhoto = profile.ProviderAvatarUrl;
        _step = Math.Clamp(profile.OnboardingStep, 0, Steps.Length - 1);
        _protectionReady = _step > 3 && settings.RecordPreviousValues;
        if (_protectionReady) { _protectionStatus = "Protection ready"; _protectionTone = StatusTone.Success; }
        NextCommand = new AsyncRelayCommand(NextAsync, () => !IsBusy);
        BackCommand = new AsyncRelayCommand(BackAsync, () => !IsBusy && Step > 0);
        SkipCommand = new AsyncRelayCommand(() => _finishLater(Step), () => !IsBusy);
        ScanCommand = new AsyncRelayCommand(ScanAsync, () => !IsBusy);
        UploadPhotoCommand = new AsyncRelayCommand(UploadPhotoAsync, () => !IsBusy);
        UseProviderPhotoCommand = new AsyncRelayCommand(UseProviderPhotoAsync, () => !IsBusy && CanUseProviderPhoto);
        RemovePhotoCommand = new AsyncRelayCommand(RemovePhotoAsync, () => !IsBusy && HasProfilePhoto);
    }

    public int Step
    {
        get => _step;
        private set
        {
            if (!Set(ref _step, Math.Clamp(value, 0, Steps.Length - 1))) return;
            Raise(nameof(Eyebrow)); Raise(nameof(Title)); Raise(nameof(Description)); Raise(nameof(Icon));
            Raise(nameof(Progress)); Raise(nameof(ProgressValue)); Raise(nameof(PrimaryLabel)); Raise(nameof(CanGoBack));
            BackCommand.NotifyCanExecuteChanged();
        }
    }
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!Set(ref _isBusy, value)) return;
            Raise(nameof(PrimaryLabel)); NextCommand.NotifyCanExecuteChanged(); BackCommand.NotifyCanExecuteChanged();
            SkipCommand.NotifyCanExecuteChanged(); ScanCommand.NotifyCanExecuteChanged(); UploadPhotoCommand.NotifyCanExecuteChanged();
            UseProviderPhotoCommand.NotifyCanExecuteChanged(); RemovePhotoCommand.NotifyCanExecuteChanged();
        }
    }
    public string Eyebrow => Steps[Step].Eyebrow;
    public IReadOnlyList<string> StepNames { get; } = ["Welcome", "Your profile", "Scan your PC", "Protection", "Gaming", "Ready"];
    public string Title => Steps[Step].Title;
    public string Description => Steps[Step].Description;
    public string Icon => MaterialIconConverter.Glyph(Steps[Step].Icon);
    public string Progress => $"STEP {Step + 1} OF {Steps.Length}";
    public int ProgressValue => Step + 1;
    public bool CanGoBack => Step > 0;
    public string PrimaryLabel => IsBusy ? Step == 2 ? "SCANNING…" : "PLEASE WAIT…" : Step switch
    {
        0 => "GET STARTED", 2 when !ScanComplete => "SCAN THIS PC", 3 when !ProtectionReady => "SET UP PROTECTION",
        5 => "ENTER HORIZON", _ => "CONTINUE"
    };
    public string DisplayName
    {
        get => _displayName;
        set { if (Set(ref _displayName, value)) Raise(nameof(Initials)); }
    }
    public string Initials => string.IsNullOrWhiteSpace(DisplayName) ? "L" : string.Concat(DisplayName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(part => part[0])).ToUpperInvariant();
    public string ProfileStatus { get => _profileStatus; private set { if (Set(ref _profileStatus, value)) Raise(nameof(IsProfileStatusVisible)); } }
    public bool IsProfileStatusVisible => !string.IsNullOrWhiteSpace(ProfileStatus);
    public string? ProfileImage => string.IsNullOrWhiteSpace(_profile.AvatarPath) ? _profile.ProviderAvatarUrl : _profile.AvatarPath;
    public bool HasProfilePhoto => !string.IsNullOrWhiteSpace(ProfileImage);
    public bool CanUseProviderPhoto => !string.IsNullOrWhiteSpace(_availableProviderPhoto) && !string.Equals(ProfileImage, _availableProviderPhoto, StringComparison.OrdinalIgnoreCase);
    public SystemSnapshot Snapshot { get => _snapshot; private set { if (Set(ref _snapshot, value)) RaiseScanProperties(); } }
    public string ScanStatus { get => _scanStatus; private set => Set(ref _scanStatus, value); }
    public bool ScanComplete { get => _scanComplete; private set { if (Set(ref _scanComplete, value)) { Raise(nameof(PrimaryLabel)); Raise(nameof(ReadyPc)); } } }
    public string StorageSummary => $"{Snapshot.StorageFree} free · {Snapshot.Storage} total";
    public string GamesSummary => Snapshot.InstalledGames is { Count: > 0 } games ? string.Join(", ", games.Keys.Select(DisplayGame)) : "None detected";
    public string RecommendationSummary => ScanComplete ? "Recommendations ready on Dashboard" : "Available after this scan";
    public bool ProtectionReady { get => _protectionReady; private set { if (Set(ref _protectionReady, value)) { Raise(nameof(PrimaryLabel)); Raise(nameof(ReadyProtection)); } } }
    public string ProtectionStatus { get => _protectionStatus; private set => Set(ref _protectionStatus, value); }
    public StatusTone ProtectionTone { get => _protectionTone; private set => Set(ref _protectionTone, value); }
    public string RestoreDataStatus => ProtectionReady ? "Ready" : "Prepared before your first change";
    public string RestorePointStatus => RestorePointCreated ? "Created" : ProtectionReady ? "Not created" : "Created when Windows supports it";
    public bool RestorePointCreated { get => _restorePointCreated; private set => Set(ref _restorePointCreated, value); }
    public bool DetectGames { get => _settings.DetectSupportedGames; set { _settings.DetectSupportedGames = value; Raise(); } }
    public bool AutomaticGaming { get => _settings.AutomaticTemporaryProfile; set { _settings.AutomaticTemporaryProfile = value; Raise(); } }
    public bool RestoreAfterGaming { get => _settings.RestoreTemporarySettings; set { _settings.RestoreTemporarySettings = value; Raise(); } }
    public string ReadyPc => ScanComplete ? $"{Snapshot.DeviceName} detected" : "PC scan can be run later";
    public string ReadyOptimisations => RecommendationSummary;
    public string ReadyProtection => ProtectionReady ? "Protection ready" : "Protection can be set up later";
    public string ReadyGames => Snapshot.InstalledGames is { Count: > 0 } ? $"{Snapshot.InstalledGames.Count} supported game{(Snapshot.InstalledGames.Count == 1 ? "" : "s")} detected" : "No supported games detected";
    public AsyncRelayCommand NextCommand { get; }
    public AsyncRelayCommand BackCommand { get; }
    public AsyncRelayCommand SkipCommand { get; }
    public AsyncRelayCommand ScanCommand { get; }
    public AsyncRelayCommand UploadPhotoCommand { get; }
    public AsyncRelayCommand UseProviderPhotoCommand { get; }
    public AsyncRelayCommand RemovePhotoCommand { get; }

    public Task InitializeAsync() => Step >= 2 ? ScanAsync() : Task.CompletedTask;

    private async Task NextAsync()
    {
        switch (Step)
        {
            case 1:
                if (!await SaveProfileAsync()) return;
                await AdvanceAsync();
                await ScanAsync();
                break;
            case 2 when !ScanComplete:
                await ScanAsync();
                break;
            case 3 when !ProtectionReady:
                await SetUpProtectionAsync();
                break;
            case 4:
                await _settingsService.SaveAsync(_settings);
                await AdvanceAsync();
                break;
            case 5:
                await _update(Step, true);
                break;
            default:
                await AdvanceAsync();
                break;
        }
    }

    private async Task AdvanceAsync()
    {
        Step++;
        await _update(Step, false);
    }

    private async Task BackAsync()
    {
        Step--;
        await _update(Step, false);
    }

    private async Task<bool> SaveProfileAsync()
    {
        if (string.IsNullOrWhiteSpace(DisplayName)) { ProfileStatus = "Enter a display name to continue."; return false; }
        ProfileStatus = string.Empty;
        IsBusy = true;
        try
        {
            var localAvatar = _profile.AvatarPath;
            var updated = await _authentication.UpdateProfileAsync(DisplayName.Trim(), _profile.ProviderAvatarUrl);
            updated.AvatarPath = localAvatar; updated.Plan = _profile.Plan; updated.DeviceName = _profile.DeviceName;
            _profile = updated;
            await _settingsService.SaveProfileAsync(_profile);
            return true;
        }
        catch (Exception exception)
        {
            ProfileStatus = exception is HorizonAuthenticationException or ArgumentException ? exception.Message : "Horizon couldn't save your profile. Try again.";
            _logger.Error("onboarding.profile", exception, nameof(OnboardingViewModel));
            return false;
        }
        finally { IsBusy = false; }
    }

    private async Task UploadPhotoAsync()
    {
        try
        {
            var path = ProfilePhotoPicker.ChooseAndStore();
            if (path is null) return;
            _profile.AvatarPath = path;
            await _settingsService.SaveProfileAsync(_profile);
            RaisePhotoProperties();
        }
        catch (Exception exception) { ProfileStatus = exception.Message; _logger.Error("onboarding.photo", exception, nameof(OnboardingViewModel)); }
    }

    private async Task UseProviderPhotoAsync()
    {
        if (string.IsNullOrWhiteSpace(_availableProviderPhoto)) return;
        try
        {
            var updated = await _authentication.UpdateProfileAsync(DisplayName.Trim(), _availableProviderPhoto);
            updated.AvatarPath = updated.ProviderAvatarUrl; updated.Plan = _profile.Plan; updated.DeviceName = _profile.DeviceName;
            _profile = updated;
            await _settingsService.SaveProfileAsync(_profile);
            RaisePhotoProperties();
        }
        catch (Exception exception) { ProfileStatus = exception is HorizonAuthenticationException ? exception.Message : "Horizon couldn't use that photo."; _logger.Error("onboarding.photo.provider", exception, nameof(OnboardingViewModel)); }
    }

    private async Task RemovePhotoAsync()
    {
        try
        {
            var updated = await _authentication.UpdateProfileAsync(DisplayName.Trim(), null);
            updated.AvatarPath = null; updated.Plan = _profile.Plan; updated.DeviceName = _profile.DeviceName;
            _profile = updated;
            await _settingsService.SaveProfileAsync(_profile);
            RaisePhotoProperties();
        }
        catch (Exception exception) { ProfileStatus = exception is HorizonAuthenticationException ? exception.Message : "Horizon couldn't remove that photo."; _logger.Error("onboarding.photo.remove", exception, nameof(OnboardingViewModel)); }
    }

    private async Task ScanAsync()
    {
        if (IsBusy) return;
        IsBusy = true; ScanStatus = "Scanning this PC…"; ScanComplete = false;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            Snapshot = await _systemInfo.RefreshAsync(timeout.Token);
            ScanComplete = true;
            ScanStatus = "PC scan complete";
            RaiseScanProperties();
        }
        catch (Exception exception)
        {
            ScanComplete = true;
            ScanStatus = "Scan finished with limited results";
            _logger.Error("onboarding.scan", exception, nameof(OnboardingViewModel));
        }
        finally { IsBusy = false; }
    }

    private async Task SetUpProtectionAsync()
    {
        if (IsBusy) return;
        IsBusy = true; ProtectionStatus = "Setting up protection…"; ProtectionTone = StatusTone.Neutral;
        _settings.RecordPreviousValues = true;
        _settings.CreateRestorePoint = true;
        RestorePointCreated = false;
        var restoreDataReady = false;
        try
        {
            await _settingsService.SaveAsync(_settings);
            restoreDataReady = true;
            if (_commandRunner.IsWindows)
            {
                if (!ScanComplete) Snapshot = await _systemInfo.ScanAsync();
                await _commandRunner.RunPowerShellAsync("Checkpoint-Computer -Description 'Horizon - Initial Setup' -RestorePointType MODIFY_SETTINGS", !Snapshot.IsAdministrator, TimeSpan.FromMinutes(3));
                RestorePointCreated = true;
            }
            ProtectionStatus = RestorePointCreated ? "Protection ready · System restore point created" : "Protection ready · Restore data ready";
            ProtectionTone = StatusTone.Success;
        }
        catch (Exception exception)
        {
            ProtectionStatus = restoreDataReady ? "Protection ready · Windows restore point not created" : "Protection couldn't be set up. Try again.";
            ProtectionTone = restoreDataReady ? StatusTone.Success : StatusTone.Warning;
            _logger.Error("onboarding.protection", exception, nameof(OnboardingViewModel));
        }
        finally
        {
            ProtectionReady = restoreDataReady;
            Raise(nameof(RestoreDataStatus)); Raise(nameof(RestorePointStatus));
            IsBusy = false;
        }
    }

    private void RaisePhotoProperties()
    {
        Raise(nameof(ProfileImage)); Raise(nameof(HasProfilePhoto)); Raise(nameof(CanUseProviderPhoto));
        UseProviderPhotoCommand.NotifyCanExecuteChanged(); RemovePhotoCommand.NotifyCanExecuteChanged();
    }

    private void RaiseScanProperties()
    {
        Raise(nameof(StorageSummary)); Raise(nameof(GamesSummary)); Raise(nameof(RecommendationSummary));
        Raise(nameof(ReadyPc)); Raise(nameof(ReadyOptimisations)); Raise(nameof(ReadyGames));
    }

    private static string DisplayGame(string value) => value.Trim().ToLowerInvariant() switch
    {
        "fortnite" => "Fortnite", "valorant" => "VALORANT", "callofduty" or "call-of-duty" => "Call of Duty",
        "cs2" => "Counter-Strike 2", "rocketleague" or "rocket-league" => "Rocket League", _ => value.Replace('-', ' ')
    };
}

public sealed class SignUpViewModel : ObservableObject
{
    private readonly IAuthenticationService _authentication;
    private readonly IAppLogger _logger;
    private readonly Func<AuthenticationResult, Task> _authenticated;
    private string _displayName = string.Empty, _email = string.Empty, _password = string.Empty, _confirmPassword = string.Empty;
    private string _statusTitle = string.Empty, _statusMessage = string.Empty;
    private StatusTone _statusTone = StatusTone.Neutral;
    private bool _isBusy, _isStatusVisible;

    public SignUpViewModel(IAuthenticationService authentication, IAppLogger logger, Func<AuthenticationResult, Task> authenticated, Action back)
    {
        _authentication = authentication; _logger = logger; _authenticated = authenticated;
        CreateCommand = new AsyncRelayCommand(CreateAsync, () => CanCreate);
        BackCommand = new RelayCommand(back, () => !IsBusy);
    }
    public string DisplayName { get => _displayName; set { if (Set(ref _displayName, value)) Changed(); } }
    public string Email { get => _email; set { if (Set(ref _email, value)) Changed(); } }
    public string Password { get => _password; set { if (Set(ref _password, value)) Changed(); } }
    public string ConfirmPassword { get => _confirmPassword; set { if (Set(ref _confirmPassword, value)) Changed(); } }
    public bool IsBusy { get => _isBusy; private set { if (Set(ref _isBusy, value)) Changed(); } }
    public bool CanCreate => !IsBusy && !string.IsNullOrWhiteSpace(DisplayName) && !string.IsNullOrWhiteSpace(Email) && Password.Length >= 10 && Password == ConfirmPassword;
    public string StatusTitle { get => _statusTitle; private set => Set(ref _statusTitle, value); }
    public string StatusMessage { get => _statusMessage; private set => Set(ref _statusMessage, value); }
    public StatusTone StatusTone { get => _statusTone; private set => Set(ref _statusTone, value); }
    public bool IsStatusVisible { get => _isStatusVisible; private set => Set(ref _isStatusVisible, value); }
    public AsyncRelayCommand CreateCommand { get; }
    public RelayCommand BackCommand { get; }
    private async Task CreateAsync()
    {
        IsBusy = true; Status("Creating account…", "Connecting securely to Horizon.", StatusTone.Neutral);
        try { await _authenticated(await _authentication.CreateAccountAsync(DisplayName, Email, Password)); }
        catch (HorizonAuthenticationException ex) { Status("Unable to create account", ex.Message, ex.State == AuthenticationState.Unavailable ? StatusTone.Warning : StatusTone.Danger); _logger.Error("auth.register", ex, nameof(SignUpViewModel)); }
        catch (ArgumentException ex) { Status("Check your details", ex.Message, StatusTone.Danger); }
        catch (Exception ex) { Status("Unable to create account", "Horizon couldn't complete registration. Try again.", StatusTone.Danger); _logger.Error("auth.register", ex, nameof(SignUpViewModel)); }
        finally { IsBusy = false; }
    }
    private void Status(string title, string message, StatusTone tone) { StatusTitle = title; StatusMessage = message; StatusTone = tone; IsStatusVisible = true; }
    private void Changed() { Raise(nameof(CanCreate)); CreateCommand.NotifyCanExecuteChanged(); BackCommand.NotifyCanExecuteChanged(); }
}

public sealed class ForgotPasswordViewModel : ObservableObject
{
    private readonly IAuthenticationService _authentication; private readonly IAppLogger _logger; private readonly Action<string, string?> _reset;
    private string _email; private string _statusMessage = string.Empty; private bool _isBusy;
    public ForgotPasswordViewModel(IAuthenticationService authentication, IAppLogger logger, string email, Action back, Action<string, string?> reset)
    { _authentication = authentication; _logger = logger; _email = email; _reset = reset; SendCommand = new AsyncRelayCommand(SendAsync, () => !IsBusy && !string.IsNullOrWhiteSpace(Email)); BackCommand = new RelayCommand(back, () => !IsBusy); }
    public string Email { get => _email; set { if (Set(ref _email, value)) Changed(); } }
    public string StatusMessage { get => _statusMessage; private set { if (Set(ref _statusMessage, value)) Raise(nameof(IsStatusVisible)); } }
    public bool IsStatusVisible => !string.IsNullOrWhiteSpace(StatusMessage);
    public bool IsBusy { get => _isBusy; private set { if (Set(ref _isBusy, value)) Changed(); } }
    public AsyncRelayCommand SendCommand { get; } public RelayCommand BackCommand { get; }
    private async Task SendAsync()
    {
        IsBusy = true; StatusMessage = "";
        try { var result = await _authentication.RequestPasswordResetAsync(Email); _reset(Email.Trim(), result.DevelopmentToken); }
        catch (Exception ex) { StatusMessage = ex is HorizonAuthenticationException or ArgumentException ? ex.Message : "Horizon couldn't request a reset code. Try again."; _logger.Error("auth.forgot-password", ex, nameof(ForgotPasswordViewModel)); }
        finally { IsBusy = false; }
    }
    private void Changed() { SendCommand.NotifyCanExecuteChanged(); BackCommand.NotifyCanExecuteChanged(); }
}

public sealed class ResetPasswordViewModel : ObservableObject
{
    private readonly IAuthenticationService _authentication; private readonly IAppLogger _logger; private readonly Action _back;
    private string _token, _password = string.Empty, _confirmPassword = string.Empty, _statusMessage = string.Empty; private bool _isBusy;
    public ResetPasswordViewModel(IAuthenticationService authentication, IAppLogger logger, string email, string? token, Action back)
    { _authentication = authentication; _logger = logger; _back = back; Email = email; _token = token ?? string.Empty; ResetCommand = new AsyncRelayCommand(ResetAsync, () => CanReset); BackCommand = new RelayCommand(back, () => !IsBusy); }
    public string Email { get; }
    public string Token { get => _token; set { if (Set(ref _token, value)) Changed(); } }
    public string Password { get => _password; set { if (Set(ref _password, value)) Changed(); } }
    public string ConfirmPassword { get => _confirmPassword; set { if (Set(ref _confirmPassword, value)) Changed(); } }
    public string StatusMessage { get => _statusMessage; private set { if (Set(ref _statusMessage, value)) Raise(nameof(IsStatusVisible)); } }
    public bool IsStatusVisible => !string.IsNullOrWhiteSpace(StatusMessage);
    public bool IsBusy { get => _isBusy; private set { if (Set(ref _isBusy, value)) Changed(); } }
    public bool CanReset => !IsBusy && !string.IsNullOrWhiteSpace(Token) && Password.Length >= 10 && Password == ConfirmPassword;
    public AsyncRelayCommand ResetCommand { get; } public RelayCommand BackCommand { get; }
    private async Task ResetAsync()
    {
        IsBusy = true; StatusMessage = "";
        try { await _authentication.ResetPasswordAsync(Token, Password); StatusMessage = "Password updated. Returning to sign in…"; await Task.Delay(900); _back(); }
        catch (Exception ex) { StatusMessage = ex is HorizonAuthenticationException or ArgumentException ? ex.Message : "Horizon couldn't reset your password. Try again."; _logger.Error("auth.reset-password", ex, nameof(ResetPasswordViewModel)); }
        finally { IsBusy = false; }
    }
    private void Changed() { Raise(nameof(CanReset)); ResetCommand.NotifyCanExecuteChanged(); BackCommand.NotifyCanExecuteChanged(); }
}
