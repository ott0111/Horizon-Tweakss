using System.Collections.ObjectModel;
using System.IO;
using Horizon.Core.Interfaces;
using Horizon.Core.Models;
using Horizon.Services;

namespace Horizon.App.ViewModels;

public abstract class PageViewModel : ObservableObject
{
    private bool _loaded;
    private bool _isBusy;
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    protected PageViewModel(string title, string subtitle) { Title = title; Subtitle = subtitle; }
    public string Title { get; protected set; }
    public string Subtitle { get; }
    public bool IsBusy { get => _isBusy; protected set => Set(ref _isBusy, value); }
    public async Task LoadAsync(bool force = false)
    {
        if (_loaded && !force) return;
        await _loadGate.WaitAsync();
        try
        {
            if (_loaded && !force) return;
            IsBusy = true;
            try { await OnLoadAsync(); _loaded = true; }
            finally { IsBusy = false; }
        }
        finally { _loadGate.Release(); }
    }
    protected virtual Task OnLoadAsync() => Task.CompletedTask;
}

public sealed record StatCard(string Label, string Value, string Detail, string Icon);
public sealed record DashboardActivity(string Title, string Detail, string Time, string Icon);

public sealed class DashboardViewModel : PageViewModel
{
    private readonly ISystemInfoService _systemInfo;
    private readonly ITweakEngine _engine;
    private readonly IHistoryService _history;
    private readonly IGamingModeService _gamingMode;
    private SystemSnapshot _snapshot = SystemSnapshot.Unknown;
    private GamingModeStatus _gamingStatus = new(false, null, null, null, null);
    private int _recommendationCount;
    private Func<NavigationDestination, Task> _navigate = _ => Task.CompletedTask;

    public DashboardViewModel(ISystemInfoService systemInfo, ITweakEngine engine, IHistoryService history, IGamingModeService gamingMode)
        : base("Good evening, Lachlan", "Here’s what matters right now.")
    {
        _systemInfo = systemInfo; _engine = engine; _history = history; _gamingMode = gamingMode;
        RescanCommand = new AsyncRelayCommand(RescanAsync);
        NavigateCommand = new AsyncRelayCommand(NavigateAsync);
    }
    public SystemSnapshot Snapshot
    {
        get => _snapshot;
        private set
        {
            if (!Set(ref _snapshot, value)) return;
            Raise(nameof(Stats)); Raise(nameof(SystemSummary)); Raise(nameof(GamingTitle)); Raise(nameof(GamingDetail));
            Raise(nameof(GamesDetected)); Raise(nameof(NetworkSummary));
        }
    }
    public IReadOnlyList<StatCard> Stats => [new("CPU", Snapshot.Cpu, $"{Snapshot.CpuCores} cores · {Snapshot.CpuThreads} threads", MaterialIconConverter.Glyph("memory")), new("GPU", Snapshot.Gpu, $"{Snapshot.Vram} · {Snapshot.GpuDriver}", MaterialIconConverter.Glyph("developer_board")), new("MEMORY", Snapshot.Ram, Snapshot.MemorySpeed, MaterialIconConverter.Glyph("memory_alt")), new("STORAGE", Snapshot.StorageFree, Snapshot.Storage, MaterialIconConverter.Glyph("hard_drive"))];
    public ObservableCollection<TweakListItem> Recommendations { get; } = [];
    public ObservableCollection<DashboardActivity> RecentActivity { get; } = [];
    public string SystemSummary => $"{Snapshot.WindowsEdition} · {Snapshot.DeviceName}";
    public string ReadinessTitle => _recommendationCount == 0 ? "Your PC is up to date" : $"{_recommendationCount} optimization{(_recommendationCount == 1 ? "" : "s")} recommended";
    public string ReadinessDetail => _recommendationCount == 0 ? "No recommended changes are waiting." : "Review the best matches for this PC.";
    public string GamingTitle => _gamingStatus.Enabled && !string.IsNullOrWhiteSpace(_gamingStatus.ActiveGame)
        ? $"{DisplayGame(_gamingStatus.ActiveGame)} mode is active"
        : GamesDetected > 0 ? $"{GamesDetected} supported game{(GamesDetected == 1 ? "" : "s")} detected" : "No supported games detected";
    public string GamingDetail => _gamingStatus.Enabled ? "Temporary settings will restore when the game closes." : GamesDetected > 0 ? "Gaming profiles are ready when you need them." : "Horizon will check again on your next scan.";
    public int GamesDetected => Snapshot.InstalledGames?.Count ?? 0;
    public string NetworkSummary => Snapshot.NetworkAdapter;
    public bool HasRecommendations => Recommendations.Count > 0;
    public bool HasRecentActivity => RecentActivity.Count > 0;
    public AsyncRelayCommand RescanCommand { get; }
    public AsyncRelayCommand NavigateCommand { get; }
    public void SetProfile(UserProfile profile)
    {
        var greeting = DateTime.Now.Hour switch { < 12 => "Good morning", < 18 => "Good afternoon", _ => "Good evening" };
        Title = $"{greeting}, {profile.DisplayName}";
        Raise(nameof(Title));
    }
    public void SetNavigate(Func<NavigationDestination, Task> navigate) => _navigate = navigate;

    protected override async Task OnLoadAsync()
    {
        var systemTask = _systemInfo.ScanAsync();
        var tweaksTask = _engine.ScanAsync();
        var historyTask = _history.GetRecentAsync(3);
        var gamingTask = _gamingMode.GetStatusAsync();
        await Task.WhenAll(systemTask, tweaksTask, historyTask, gamingTask);

        Snapshot = await systemTask;
        _gamingStatus = await gamingTask;
        var recommended = (await tweaksTask).Where(x => x.Definition.Recommended && x.Status.Compatible && x.Status.State is TweakApplyState.Available or TweakApplyState.AdminRequired).ToList();
        _recommendationCount = recommended.Count;
        Recommendations.Clear();
        foreach (var item in recommended.Take(3)) Recommendations.Add(item);

        RecentActivity.Clear();
        foreach (var session in await historyTask)
        {
            var icon = session.Source.Contains("restore", StringComparison.OrdinalIgnoreCase) ? "restore" : session.Source.Contains("gaming", StringComparison.OrdinalIgnoreCase) ? "sports_esports" : "task_alt";
            RecentActivity.Add(new(session.Name, $"{session.ChangeCount} change{(session.ChangeCount == 1 ? "" : "s")}", RelativeTime(session.CompletedAt ?? session.StartedAt), MaterialIconConverter.Glyph(icon)));
        }
        Raise(nameof(ReadinessTitle)); Raise(nameof(ReadinessDetail)); Raise(nameof(GamingTitle)); Raise(nameof(GamingDetail));
        Raise(nameof(HasRecommendations)); Raise(nameof(HasRecentActivity));
    }

    private async Task RescanAsync()
    {
        Snapshot = await _systemInfo.RefreshAsync();
        await _engine.RefreshScanAsync();
        await LoadAsync(true);
    }

    private Task NavigateAsync(object? value) => Enum.TryParse<NavigationDestination>(value?.ToString(), out var destination) ? _navigate(destination) : Task.CompletedTask;
    private static string RelativeTime(DateTimeOffset value)
    {
        var local = value.ToLocalTime();
        var today = DateTimeOffset.Now.Date;
        return local.Date == today ? $"Today, {local:h:mm tt}" : local.Date == today.AddDays(-1) ? $"Yesterday, {local:h:mm tt}" : local.ToString("d MMM");
    }
    private static string DisplayGame(string value) => value.Equals("fortnite", StringComparison.OrdinalIgnoreCase) ? "Fortnite" : value.Replace('-', ' ');
}

public sealed class RecommendationsViewModel : PageViewModel
{
    private readonly ITweakEngine _engine;
    private readonly IToastService _toasts;
    private readonly IDialogService _dialogs;
    public RecommendationsViewModel(ITweakEngine engine, IToastService toasts, IDialogService dialogs) : base("Recommended for your PC", "Horizon evaluates your hardware and Windows configuration before suggesting changes.") { _engine = engine; _toasts = toasts; _dialogs = dialogs; ApplyCommand = new AsyncRelayCommand(ApplyAsync); ApplyAllCommand = new AsyncRelayCommand(ApplyAllAsync); }
    public ObservableCollection<TweakListItem> Items { get; } = [];
    public AsyncRelayCommand ApplyCommand { get; }
    public AsyncRelayCommand ApplyAllCommand { get; }
    public string Summary => $"{Items.Count} recommendations · {Items.Count(x => x.Definition.Risk == TweakRisk.Low)} safe · {Items.Count(x => x.Definition.Risk == TweakRisk.Advanced)} advanced";
    protected override async Task OnLoadAsync() { Items.Clear(); foreach (var item in (await _engine.ScanAsync()).Where(x => x.Definition.Recommended && x.Status.Compatible && x.Status.State is TweakApplyState.Available or TweakApplyState.AdminRequired).Take(9)) Items.Add(item); Raise(nameof(Summary)); }
    private async Task ApplyAsync(object? parameter) { if (parameter is not TweakListItem item) return; if (item.Definition.RequiresAdmin && !await _dialogs.ShowAsync(DialogKind.AdministratorAccess, "ADMINISTRATOR ACCESS REQUIRED", "This optimization requires elevated Windows permission. Horizon only requests administrator access when required. Windows will show its own permission dialog next.")) return; var session = await _engine.ApplyAsync([item.Definition.Id]); var result = session.Changes.FirstOrDefault(); _toasts.Show(result?.Result == TweakChangeResult.Applied ? $"Applied and verified {item.Definition.Name}" : result?.Error ?? $"No change was required for {item.Definition.Name}", result?.Result == TweakChangeResult.Failed ? StatusTone.Danger : StatusTone.Success); await LoadAsync(true); }
    private async Task ApplyAllAsync() { var candidates = Items.Where(x => x.Status.State is TweakApplyState.Available or TweakApplyState.AdminRequired && x.Definition.Risk != TweakRisk.Advanced).ToList(); var adminCount = candidates.Count(x => x.Definition.RequiresAdmin); if (adminCount > 0 && !await _dialogs.ShowAsync(DialogKind.AdministratorAccess, "ADMINISTRATOR ACCESS REQUIRED", $"{adminCount} selected optimizations require elevated Windows permission. Horizon only requests administrator access when required. Windows will show its own permission dialog next.")) return; var session = await _engine.ApplyAsync(candidates.Select(x => x.Definition.Id)); var applied = session.Changes.Count(change => change.Result == TweakChangeResult.Applied); _toasts.Show(session.Success ? $"Applied and verified {applied} safe recommendations" : "Optimization completed with errors", session.Success ? StatusTone.Success : StatusTone.Danger); await LoadAsync(true); }
}

public sealed class TweaksViewModel : PageViewModel
{
    private readonly ITweakEngine _engine;
    private readonly IToastService _toasts;
    private readonly List<TweakListItem> _all = [];
    private string _search = string.Empty;
    private string _category = "All";
    private bool _filterOpen;
    private bool _recommendedOnly;
    private bool _appliedOnly;
    private bool _notAppliedOnly;
    private string _risk = "Any risk";
    private string _status = "Any status";
    private TweakListItem? _selected;

    public TweaksViewModel(ITweakEngine engine, IToastService toasts) : base("All tweaks", "Find the right settings for your PC.")
    {
        _engine = engine; _toasts = toasts;
        ToggleFilterCommand = new RelayCommand(() => IsFilterOpen = !IsFilterOpen);
        CloseFilterCommand = new RelayCommand(() => IsFilterOpen = false);
        SelectCommand = new RelayCommand(x => Selected = x as TweakListItem);
        ApplySelectedCommand = new AsyncRelayCommand(ApplySelectedAsync, CanApplySelected);
        ClearFiltersCommand = new RelayCommand(() => { RecommendedOnly = AppliedOnly = NotAppliedOnly = false; Category = "All"; Risk = "Any risk"; Status = "Any status"; });
    }
    public ObservableCollection<TweakListItem> Items { get; } = [];
    public IReadOnlyList<string> Categories { get; } = ["All", "Windows", "CPU", "GPU", "Memory", "Storage", "Network", "Input", "Power", "Gaming", "Fortnite", "Cleanup", "Testing", "BIOS", "Games", "Streaming", "System"];
    public IReadOnlyList<string> Risks { get; } = ["Any risk", "Low", "Medium", "Advanced"];
    public IReadOnlyList<string> Statuses { get; } = ["Any status", "Ready", "Applied", "Unavailable"];
    public string Search { get => _search; set { if (Set(ref _search, value)) ApplyFilter(); } }
    public string Category { get => _category; set { if (Set(ref _category, value)) ApplyFilter(); } }
    public bool IsFilterOpen { get => _filterOpen; set => Set(ref _filterOpen, value); }
    public bool RecommendedOnly { get => _recommendedOnly; set { if (Set(ref _recommendedOnly, value)) ApplyFilter(); } }
    public bool AppliedOnly { get => _appliedOnly; set { if (Set(ref _appliedOnly, value)) ApplyFilter(); } }
    public bool NotAppliedOnly { get => _notAppliedOnly; set { if (Set(ref _notAppliedOnly, value)) ApplyFilter(); } }
    public string Risk { get => _risk; set { if (Set(ref _risk, value)) ApplyFilter(); } }
    public string Status { get => _status; set { if (Set(ref _status, value)) ApplyFilter(); } }
    public TweakListItem? Selected
    {
        get => _selected;
        set
        {
            if (!Set(ref _selected, value)) return;
            ApplySelectedCommand.NotifyCanExecuteChanged();
            Raise(nameof(SelectedCompatibility)); Raise(nameof(SelectedRestart)); Raise(nameof(SelectedMode));
            Raise(nameof(SelectedRestore)); Raise(nameof(SelectedActionLabel)); Raise(nameof(SelectedActionAvailable)); Raise(nameof(SelectedActionHelp));
        }
    }
    public int FilterCount => (RecommendedOnly ? 1 : 0) + (AppliedOnly ? 1 : 0) + (NotAppliedOnly ? 1 : 0) + (Category == "All" ? 0 : 1) + (Risk == "Any risk" ? 0 : 1) + (Status == "Any status" ? 0 : 1);
    public string SelectedCompatibility => Selected?.Status.Compatible == true ? "Compatible" : Selected?.Status.CompatibilityReason ?? "Unavailable";
    public string SelectedRestart => Selected?.Definition.Restart is null or RestartRequirement.None ? "Not required" : Selected.Definition.Restart.ToString();
    public string SelectedMode => Selected?.Definition.Temporary == true ? "Temporary" : "Persistent";
    public string SelectedRestore => Selected?.Status.HasActiveBackup == true ? "Ready to restore" : Selected?.Definition.Actionable == true ? "Available after applying" : "Not applicable";
    public string SelectedActionLabel => Selected?.Status.HasActiveBackup == true ? "RESTORE" : Selected?.Definition.Actionable == true ? "APPLY" : "GUIDED REVIEW";
    public bool SelectedActionAvailable => CanApplySelected();
    public string SelectedActionHelp => Selected?.Definition.Actionable != true
        ? "This item is guidance only and does not make an automatic change."
        : Selected?.Status.CompatibilityReason ?? "This change is unavailable on this PC.";
    public RelayCommand ToggleFilterCommand { get; }
    public RelayCommand CloseFilterCommand { get; }
    public RelayCommand SelectCommand { get; }
    public RelayCommand ClearFiltersCommand { get; }
    public AsyncRelayCommand ApplySelectedCommand { get; }
    protected override async Task OnLoadAsync() { _all.Clear(); _all.AddRange(await _engine.ScanAsync()); ApplyFilter(); Selected ??= Items.FirstOrDefault(); }
    private void ApplyFilter()
    {
        if (_all.Count == 0) return;
        var selectedId = Selected?.Definition.Id;
        var query = _all.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(Search)) query = query.Where(x => x.Definition.Name.Contains(Search, StringComparison.OrdinalIgnoreCase) || x.Definition.Description.Contains(Search, StringComparison.OrdinalIgnoreCase));
        if (Category != "All") query = query.Where(x => x.Definition.Category.StartsWith(Category, StringComparison.OrdinalIgnoreCase));
        if (Enum.TryParse<TweakRisk>(Risk, out var parsedRisk)) query = query.Where(x => x.Definition.Risk == parsedRisk);
        query = Status switch
        {
            "Ready" => query.Where(x => x.Status.State is TweakApplyState.Available or TweakApplyState.AdminRequired),
            "Applied" => query.Where(x => x.Status.State is TweakApplyState.Applied or TweakApplyState.AlreadyApplied),
            "Unavailable" => query.Where(x => x.Status.State is TweakApplyState.Unsupported or TweakApplyState.Locked or TweakApplyState.UpdateChanged or TweakApplyState.Failed),
            _ => query
        };
        if (RecommendedOnly) query = query.Where(x => x.Definition.Recommended);
        if (AppliedOnly) query = query.Where(x => x.Status.State is TweakApplyState.Applied or TweakApplyState.AlreadyApplied);
        if (NotAppliedOnly) query = query.Where(x => x.Status.State == TweakApplyState.Available);
        Items.Clear();
        foreach (var item in query.OrderByDescending(x => x.Definition.Recommended).ThenBy(x => x.Definition.Name)) Items.Add(item);
        Selected = selectedId is null ? Items.FirstOrDefault() : Items.FirstOrDefault(x => x.Definition.Id == selectedId) ?? Items.FirstOrDefault();
        Raise(nameof(FilterCount));
    }
    private bool CanApplySelected() => Selected is { Definition.Actionable: true } && Selected.Status.State is not (TweakApplyState.Unsupported or TweakApplyState.Locked);
    private async Task ApplySelectedAsync() { if (Selected is null) return; if (!Selected.Definition.Actionable) { _toasts.Show("This item is a guided review and does not make an automatic change", StatusTone.Warning); return; } if (Selected.Status.State is TweakApplyState.Unsupported or TweakApplyState.Locked) { _toasts.Show(Selected.Status.CompatibilityReason ?? "This tweak is unavailable", StatusTone.Warning); return; } var restoring = Selected.Status.HasActiveBackup; var session = restoring ? await _engine.RestoreAsync([Selected.Definition.Id]) : await _engine.ApplyAsync([Selected.Definition.Id]); var result = session.Changes.FirstOrDefault(); _toasts.Show(result?.Error ?? (restoring ? "Tweak restored and verified" : result?.Result == TweakChangeResult.AlreadyApplied ? "The desired value was already present" : "Tweak applied and verified"), result?.Result == TweakChangeResult.Failed ? StatusTone.Danger : StatusTone.Success); var selectedId = Selected.Definition.Id; await LoadAsync(true); Selected = Items.FirstOrDefault(x => x.Definition.Id == selectedId); }
}

public sealed class FortniteViewModel : PageViewModel
{
    private readonly IGameDetectionService _games; private readonly ITweakEngine _engine; private readonly IGamingModeService _gamingMode; private readonly IToastService _toasts;
    private bool _detected; private string _path = "Not detected"; private bool _temporary;
    public FortniteViewModel(IGameDetectionService games, ITweakEngine engine, IGamingModeService gamingMode, IToastService toasts) : base("Fortnite", "A compatible profile built around your hardware and current game installation.")
    { _games = games; _engine = engine; _gamingMode = gamingMode; _toasts = toasts; ApplyCommand = new AsyncRelayCommand(ApplyAsync); }
    public bool Detected { get => _detected; private set => Set(ref _detected, value); }
    public string InstallPath { get => _path; private set => Set(ref _path, value); }
    public bool TemporaryMode { get => _temporary; set => Set(ref _temporary, value); }
    public AsyncRelayCommand ApplyCommand { get; }
    protected override async Task OnLoadAsync() { var found = await _games.DetectAsync(); Detected = found.TryGetValue("fortnite", out var path); InstallPath = path ?? "Fortnite was not detected on this PC."; var status = await _gamingMode.GetStatusAsync(); TemporaryMode = status.Enabled; }
    private async Task ApplyAsync()
    {
        if (!Detected) { _toasts.Show("Fortnite was not detected", StatusTone.Warning); return; }
        var session = await _engine.ApplySessionAsync(["win.game-mode", "win.game-dvr", "fortnite.vsync", "fortnite.grass"], "Fortnite Competitive Profile", "fortnite");
        await _gamingMode.ConfigureAsync("fortnite", TemporaryMode, ["fortnite.temporary-power", "fortnite.temporary-dvr"]);
        _toasts.Show(session.Success ? $"Verified {session.Changes.Count(change => change.Result == TweakChangeResult.Applied)} Fortnite changes" : "Fortnite profile completed with errors", session.Success ? StatusTone.Success : StatusTone.Danger);
    }
}

public sealed class SelectablePackageViewModel : ObservableObject { private bool _selected; public SelectablePackageViewModel(InstalledPackage package) { Package = package; _selected = package.Selected && package.Removable; } public InstalledPackage Package { get; } public bool IsSelected { get => _selected; set { if (Package.Removable) Set(ref _selected, value); } } }
public sealed class DebloatViewModel : PageViewModel
{
    private readonly IDebloatService _service; private readonly IToastService _toasts; private readonly IDialogService _dialogs;
    public DebloatViewModel(IDebloatService service, IToastService toasts, IDialogService dialogs) : base("Debloat", "Review optional Windows applications individually before removal.") { _service = service; _toasts = toasts; _dialogs = dialogs; RemoveCommand = new AsyncRelayCommand(RemoveAsync); }
    public ObservableCollection<SelectablePackageViewModel> Items { get; } = []; public AsyncRelayCommand RemoveCommand { get; }
    protected override async Task OnLoadAsync() { Items.Clear(); foreach (var item in await _service.GetInstalledPackagesAsync()) Items.Add(new(item)); }
    private async Task RemoveAsync() { var selected = Items.Where(x => x.IsSelected && x.Package.Removable).Select(x => x.Package.Id).ToList(); if (selected.Count == 0) { _toasts.Show("Select at least one removable application", StatusTone.Warning); return; } if (!await _dialogs.ShowAsync(DialogKind.GeneralError, "REMOVE SELECTED APPLICATIONS?", $"Windows will remove {selected.Count} selected current-user application packages. App-local data may be discarded.", "REMOVE", tone: StatusTone.Danger)) return; await _service.RemoveAsync(selected); _toasts.Show($"Removed {selected.Count} selected applications", StatusTone.Success); await LoadAsync(true); }
}

public sealed class StartupItemViewModel : ObservableObject { private bool _enabled; private readonly IStartupService _service; public StartupItemViewModel(StartupApplication app, IStartupService service) { App = app; _service = service; _enabled = app.Enabled; ToggleCommand = new AsyncRelayCommand(ToggleAsync); } public StartupApplication App { get; } public bool Enabled { get => _enabled; set => Set(ref _enabled, value); } public AsyncRelayCommand ToggleCommand { get; } private async Task ToggleAsync() { var requested = Enabled; try { await _service.SetEnabledAsync(App.Id, requested); } catch { Enabled = !requested; throw; } } }
public sealed class StartupViewModel : PageViewModel
{
    private readonly IStartupService _service; public StartupViewModel(IStartupService service) : base("Startup manager", "Control supported applications that start when you sign in.") { _service = service; }
    public ObservableCollection<StartupItemViewModel> Items { get; } = []; protected override async Task OnLoadAsync() { Items.Clear(); foreach (var item in await _service.GetApplicationsAsync()) Items.Add(new(item, _service)); }
}

public sealed class CleanupItemViewModel : ObservableObject { private bool _selected; public CleanupItemViewModel(CleanupCategory category, Action changed) { Category = category; _selected = category.IsSelected; _changed = changed; } private readonly Action _changed; public CleanupCategory Category { get; } public bool IsSelected { get => _selected; set { if (Set(ref _selected, value)) { Category.IsSelected = value; _changed(); } } } }
public sealed class CleanupViewModel : PageViewModel
{
    private readonly ICleanupService _service; private readonly IToastService _toasts; private readonly IDialogService _dialogs; private CleanupState _state = CleanupState.Scanning; private double _progress; private long _cleaned;
    public CleanupViewModel(ICleanupService service, IToastService toasts, IDialogService dialogs) : base("System cleanup", "Review safe cleanup categories before removing files.") { _service = service; _toasts = toasts; _dialogs = dialogs; ScanCommand = new AsyncRelayCommand(ScanAsync); CleanCommand = new AsyncRelayCommand(CleanAsync, () => SelectedBytes > 0); }
    public ObservableCollection<CleanupItemViewModel> Items { get; } = []; public CleanupState State { get => _state; private set => Set(ref _state, value); } public double Progress { get => _progress; private set => Set(ref _progress, value); } public long SelectedBytes => Items.Where(x => x.IsSelected).Sum(x => x.Category.DetectedBytes); public string SelectedSize => SizeFormatter.Format(SelectedBytes); public string CleanActionLabel => $"CLEAN {SelectedSize}"; public string CleanedSize => SizeFormatter.Format(_cleaned); public AsyncRelayCommand ScanCommand { get; } public AsyncRelayCommand CleanCommand { get; }
    protected override Task OnLoadAsync() => ScanAsync();
    private async Task ScanAsync() { try { State = CleanupState.Scanning; Progress = 0; var values = await _service.ScanAsync(new Progress<double>(x => Progress = x * 100)); Items.Clear(); foreach (var value in values) Items.Add(new(value, SelectionChanged)); State = CleanupState.Ready; SelectionChanged(); } catch { State = CleanupState.Error; } }
    private async Task CleanAsync() { if (!await _dialogs.ShowAsync(DialogKind.GeneralError, "CLEAN SELECTED FILES?", $"Remove {SelectedSize} from the selected temporary folders? This cannot be undone.", "CLEAN", tone: StatusTone.Warning)) return; try { State = CleanupState.Cleaning; _cleaned = await _service.CleanAsync(Items.Where(x => x.IsSelected).Select(x => x.Category), new Progress<double>(x => Progress = x * 100)); Raise(nameof(CleanedSize)); var values = await _service.ScanAsync(); Items.Clear(); foreach (var value in values) Items.Add(new(value, SelectionChanged)); State = CleanupState.Complete; SelectionChanged(); _toasts.Show($"Cleaned {CleanedSize}", StatusTone.Success); } catch { State = CleanupState.Error; _toasts.Show("Cleanup couldn't finish", StatusTone.Danger); } }
    private void SelectionChanged() { Raise(nameof(SelectedBytes)); Raise(nameof(SelectedSize)); Raise(nameof(CleanActionLabel)); CleanCommand.NotifyCanExecuteChanged(); }
}

public sealed class SystemViewModel : PageViewModel
{
    private readonly ISystemInfoService _service; private SystemSnapshot _snapshot = SystemSnapshot.Unknown; public SystemViewModel(ISystemInfoService service) : base("System", "Detected hardware and Windows information used for compatibility decisions.") { _service = service; RescanCommand = new AsyncRelayCommand(RescanAsync); }
    public SystemSnapshot Snapshot { get => _snapshot; private set => Set(ref _snapshot, value); } public AsyncRelayCommand RescanCommand { get; } protected override async Task OnLoadAsync() => Snapshot = await _service.ScanAsync();
    private async Task RescanAsync() => Snapshot = await _service.RefreshAsync();
}

public sealed record CompatibilityCheck(string Name, string Detail, StatusTone Tone, string Status);
public sealed class CompatibilityViewModel : PageViewModel
{
    private readonly ITweakEngine _engine; public CompatibilityViewModel(ITweakEngine engine) : base("Compatibility", "See why each optimization path is available or unavailable on this PC.") { _engine = engine; }
    public ObservableCollection<CompatibilityCheck> Checks { get; } = [];
    public int TotalCount { get; private set; }
    public int SupportedCount { get; private set; }
    public int ReadyCount { get; private set; }
    public int AttentionCount { get; private set; }
    public string OverallSummary => $"{SupportedCount} of {TotalCount} optimization paths are supported";
    protected override async Task OnLoadAsync()
    {
        Checks.Clear();
        var items = await _engine.ScanAsync();
        TotalCount = items.Count;
        SupportedCount = items.Count(item => item.Status.State is not (TweakApplyState.Unsupported or TweakApplyState.Locked or TweakApplyState.UpdateChanged or TweakApplyState.Failed));
        ReadyCount = items.Count(item => item.Status.State is TweakApplyState.Available or TweakApplyState.AdminRequired);
        AttentionCount = TotalCount - SupportedCount;
        foreach (var item in items)
        {
            var tone = item.Status.State switch
            {
                TweakApplyState.Failed => StatusTone.Danger,
                TweakApplyState.Unsupported or TweakApplyState.Locked or TweakApplyState.UpdateChanged or TweakApplyState.AdminRequired => StatusTone.Warning,
                TweakApplyState.Applied or TweakApplyState.AlreadyApplied => StatusTone.Success,
                _ => StatusTone.Neutral
            };
            var detail = item.Status.CompatibilityReason ?? $"{item.Definition.Category} · Current: {item.Status.CurrentValue} · Horizon: {item.Status.HorizonValue}";
            Checks.Add(new(item.Definition.Name, detail, tone, DisplayState(item.Status.State)));
        }
        Raise(nameof(TotalCount)); Raise(nameof(SupportedCount)); Raise(nameof(ReadyCount)); Raise(nameof(AttentionCount)); Raise(nameof(OverallSummary));
    }

    private static string DisplayState(TweakApplyState state) => state switch
    {
        TweakApplyState.AlreadyApplied => "APPLIED",
        TweakApplyState.AdminRequired => "ADMIN REQUIRED",
        TweakApplyState.UpdateChanged => "REVIEW",
        TweakApplyState.Locked => "PLAN REQUIRED",
        TweakApplyState.Failed => "CHECK FAILED",
        _ => state.ToString().ToUpperInvariant()
    };
}

public sealed class BenchmarkViewModel : PageViewModel
{
    private readonly IBenchmarkService _service; private BenchmarkState _state = BenchmarkState.NoData; private BenchmarkResult? _result; private double _progress;
    public BenchmarkViewModel(IBenchmarkService service) : base("Benchmark", "Capture a lightweight local baseline before and after optimization.") { _service = service; RunCommand = new AsyncRelayCommand(RunAsync); }
    public BenchmarkState State { get => _state; private set => Set(ref _state, value); } public BenchmarkResult? Result { get => _result; private set { if (Set(ref _result, value)) Raise(nameof(Score)); } } public string Score => Result is null ? "—" : $"{Result.OperationsPerSecond / 1_000_000:0.00}M"; public double Progress { get => _progress; private set => Set(ref _progress, value); } public AsyncRelayCommand RunCommand { get; }
    private async Task RunAsync() { try { State = BenchmarkState.Running; Progress = 0; Result = await _service.RunAsync(new Progress<double>(value => Progress = value * 100)); State = BenchmarkState.Complete; } catch { State = BenchmarkState.Failed; } }
}

public sealed class RestoreViewModel : PageViewModel
{
    private readonly IRestoreService _restore; private readonly IHistoryService _history; private readonly IActiveChangeStore _activeChanges; private readonly IToastService _toasts; private readonly IDialogService _dialogs;
    public RestoreViewModel(IRestoreService restore, IHistoryService history, IActiveChangeStore activeChanges, IToastService toasts, IDialogService dialogs) : base("Restore", "Return supported Horizon changes to their recorded previous values.") { _restore = restore; _history = history; _activeChanges = activeChanges; _toasts = toasts; _dialogs = dialogs; UndoCommand = new AsyncRelayCommand(UndoAsync); RestoreAllCommand = new AsyncRelayCommand(RestoreAllAsync); RestoreSessionCommand = new AsyncRelayCommand(RestoreSessionAsync); }
    public ObservableCollection<OptimizationSession> Sessions { get; } = []; public AsyncRelayCommand UndoCommand { get; } public AsyncRelayCommand RestoreAllCommand { get; } public AsyncRelayCommand RestoreSessionCommand { get; }
    protected override async Task OnLoadAsync() { Sessions.Clear(); var activeSessionIds = (await _activeChanges.GetAllAsync()).Values.Select(change => change.SessionId).ToHashSet(); foreach (var item in (await _history.GetRecentAsync(500)).Where(session => activeSessionIds.Contains(session.Id))) Sessions.Add(item); }
    private async Task UndoAsync() { var session = await _restore.UndoLastAsync(); _toasts.Show(session is null ? "No optimization session to restore" : $"Restored {session.ChangeCount} changes", session is null ? StatusTone.Warning : StatusTone.Success); await LoadAsync(true); }
    private async Task RestoreAllAsync() { if (!await _dialogs.ShowAsync(DialogKind.RestoreConfirmation, "RESTORE ALL CHANGES?", "Horizon will return every supported active tweak to its recorded previous value.", "RESTORE ALL")) return; var session = await _restore.RestoreAllAsync(); _toasts.Show($"Restored {session.ChangeCount} recorded changes", StatusTone.Success); await LoadAsync(true); }
    private async Task RestoreSessionAsync(object? parameter) { if (parameter is not OptimizationSession selected) return; if (!await _dialogs.ShowAsync(DialogKind.RestoreConfirmation, "RESTORE THIS SESSION?", $"Horizon will restore active changes from {selected.Name} to their exact recorded values.", "RESTORE SESSION")) return; var session = await _restore.RestoreSessionAsync(selected.Id); _toasts.Show(session.Success ? $"Restored {session.Changes.Count(change => change.Result == TweakChangeResult.Restored)} changes" : "Session restore completed with errors", session.Success ? StatusTone.Success : StatusTone.Danger); await LoadAsync(true); }
}

public sealed class HistoryViewModel : PageViewModel
{
    private readonly IHistoryService _history; private readonly IDialogService _dialogs; public HistoryViewModel(IHistoryService history, IDialogService dialogs) : base("History", "A local record of optimization and restore sessions.") { _history = history; _dialogs = dialogs; ClearCommand = new AsyncRelayCommand(ClearAsync); }
    public ObservableCollection<OptimizationSession> Sessions { get; } = []; public AsyncRelayCommand ClearCommand { get; } protected override async Task OnLoadAsync() { Sessions.Clear(); foreach (var item in await _history.GetRecentAsync()) Sessions.Add(item); } private async Task ClearAsync() { if (!await _dialogs.ShowAsync(DialogKind.DeleteHistory, "DELETE LOCAL HISTORY?", "This removes local session history. Active restore data is kept separately.", "DELETE", tone: StatusTone.Danger)) return; await _history.ClearAsync(); Sessions.Clear(); }
}

public sealed record PlanOption(string ProductId, string Name, string Price, string Description, IReadOnlyList<string> Features, bool Recommended = false);
public sealed class PlanViewModel : PageViewModel
{
    private readonly IPurchaseService _purchase; private readonly IToastService _toasts; public PlanViewModel(IPurchaseService purchase, IToastService toasts) : base("Choose your plan", "Unlock deeper hardware-aware optimizations when you are ready.")
    {
        _purchase = purchase; _toasts = toasts; PurchaseCommand = new AsyncRelayCommand(BeginPurchaseAsync);
        Plans =
        [
            new("starter", "Starter", "FREE", "Safe essentials", ["System and compatibility scan", "Starter tweak catalogue", "Startup, cleanup, and debloat"]),
            new("performance", "Performance", "$19.99", "The recommended complete performance package", ["Everything in Starter", "CPU, GPU, memory, storage, network", "Fortnite profile and measured testing"], true),
            new("ultimate", "Ultimate", "$39.99", "Personalized advanced workflows", ["Everything in Performance", "BIOS, advanced tuning, custom debloat", "Full diagnostics and game profiles"]),
            new("bios", "BIOS / Advanced BIOS", "$14.99", "Motherboard-specific performance workflow", ["XMP / EXPO / DOCP", "Resizable BAR / SAM", "Stability verification"]),
            new("ram", "RAM Tuning", "$19.99", "Memory frequency and timing workflow", ["Timing and frequency review", "Voltage guidance where supported", "Measured stability workflow"]),
            new("cpu", "CPU Tuning", "$19.99", "Power, thermal, and undervolt workflow", ["Power-limit review", "Performance / temperature balance", "Stability testing"]),
            new("gpu", "GPU Tuning", "$14.99", "Vendor-specific GPU tuning workflow", ["Power, clock, and voltage review", "Driver optimization", "Stability testing"]),
            new("network", "Network Optimization", "$14.99", "Advanced adapter and latency workflow", ["Adapter and TCP/IP tuning", "Ping, jitter, and packet loss", "Router / QoS recommendations"]),
            new("game-fortnite", "Game Optimization", "$7.99 / game", "Per-game configuration and launch workflow", ["Config files", "Launch options", "GPU profile and frame-time review"]),
            new("pc-checkup", "PC Checkup", "$9.99", "Hardware and Windows health review", ["Storage health", "Bottleneck identification", "Measured recommendations"]),
            new("stream", "Stream Setup", "$14.99", "OBS encoder, bitrate, audio, and performance review", ["Encoder configuration", "Audio and background-noise setup", "Performance testing"])
        ];
    }
    public IReadOnlyList<PlanOption> Plans { get; } public AsyncRelayCommand PurchaseCommand { get; }
    public IReadOnlyList<PlanOption> PrimaryPlans => Plans.Take(3).ToArray();
    public IReadOnlyList<PlanOption> AdditionalPlans => Plans.Skip(3).ToArray();
    private async Task BeginPurchaseAsync(object? value)
    {
        try { await _purchase.BeginPurchaseAsync(value?.ToString() ?? "performance"); }
        catch (Exception exception) { _toasts.Show(exception.Message, StatusTone.Warning); }
    }
}

public sealed class SettingsViewModel : PageViewModel
{
    private readonly ISettingsService _service;
    private readonly IUpdateService _updates;
    private readonly ILaunchAtStartupService _launchAtStartup;
    private readonly IGamingModeService _gamingMode;
    private readonly IToastService _toasts;
    private AppSettings _settings = new();
    private string _section = "General";
    private Action<AppSettings> _settingsUpdated = _ => { };

    public SettingsViewModel(ISettingsService service, IUpdateService updates, ILaunchAtStartupService launchAtStartup,
        IGamingModeService gamingMode, IToastService toasts) : base("Settings", "Choose how Horizon behaves on this PC.")
    {
        _service = service; _updates = updates; _launchAtStartup = launchAtStartup; _gamingMode = gamingMode; _toasts = toasts;
        SaveCommand = new AsyncRelayCommand(SaveAsync);
        CheckUpdatesCommand = new AsyncRelayCommand(CheckAsync);
        SelectSectionCommand = new RelayCommand(x => Section = x?.ToString() ?? "General");
    }
    public AppSettings Settings { get => _settings; private set => Set(ref _settings, value); }
    public string Section { get => _section; set => Set(ref _section, value); }
    public IReadOnlyList<string> Sections { get; } = ["General", "Optimization", "Gaming", "Notifications", "Appearance", "Updates", "Privacy", "Advanced", "About"];
    public AsyncRelayCommand SaveCommand { get; }
    public AsyncRelayCommand CheckUpdatesCommand { get; }
    public RelayCommand SelectSectionCommand { get; }
    public void SetSettingsUpdated(Action<AppSettings> settingsUpdated) => _settingsUpdated = settingsUpdated;

    protected override async Task OnLoadAsync()
    {
        Settings = await _service.LoadAsync();
        Settings.LaunchWithWindows = await _launchAtStartup.GetEnabledAsync();
    }

    private async Task SaveAsync()
    {
        bool? previousLaunchPreference = null;
        try
        {
            previousLaunchPreference = await _launchAtStartup.GetEnabledAsync();
            await _launchAtStartup.SetEnabledAsync(Settings.LaunchWithWindows);
            await _gamingMode.ConfigureAsync("fortnite", Settings.AutomaticTemporaryProfile,
                ["fortnite.temporary-power", "fortnite.temporary-dvr"]);
            await _service.SaveAsync(Settings);
            _settingsUpdated(Settings);
            _toasts.Show("Settings saved", StatusTone.Success);
        }
        catch
        {
            if (previousLaunchPreference is bool previous)
                try { await _launchAtStartup.SetEnabledAsync(previous); } catch { }
            _toasts.Show("Horizon couldn't save every setting. No Windows startup change was left half-finished.", StatusTone.Danger);
        }
    }

    private async Task CheckAsync()
    {
        try
        {
            var update = await _updates.CheckAsync();
            _toasts.Show(update is null ? "Horizon is up to date" : $"Horizon {update} is available",
                update is null ? StatusTone.Success : StatusTone.Warning);
        }
        catch
        {
            _toasts.Show("Horizon couldn't check for updates right now.", StatusTone.Warning);
        }
    }
}

public sealed class SupportViewModel : PageViewModel
{
    private readonly ISystemInfoService _systemInfo;
    private readonly IToastService _toasts;
    private string _message = string.Empty;
    public SupportViewModel(ISystemInfoService systemInfo, IToastService toasts) : base("Support", "Get clear answers and share diagnostics only when you choose.")
    {
        _systemInfo = systemInfo; _toasts = toasts;
        OpenDiscordCommand = new RelayCommand(OpenDiscord);
        SendCommand = new RelayCommand(Send);
        SelectTopicCommand = new RelayCommand(SelectTopic);
        ExportDiagnosticCommand = new AsyncRelayCommand(ExportDiagnosticAsync);
    }
    public IReadOnlyList<string> Topics { get; } = ["Optimization issue", "Restore issue", "Account or purchase", "Other"];
    public string Message { get => _message; set => Set(ref _message, value); }
    public RelayCommand OpenDiscordCommand { get; }
    public RelayCommand SendCommand { get; }
    public RelayCommand SelectTopicCommand { get; }
    public AsyncRelayCommand ExportDiagnosticCommand { get; }

    private void SelectTopic(object? value)
    {
        var topic = value?.ToString();
        if (string.IsNullOrWhiteSpace(topic)) return;
        Message = $"{topic}\n\nWhat happened:\n\nWhat I expected:\n";
    }

    private void OpenDiscord()
    {
        try { ExternalLinks.Open(ExternalLinks.DiscordSupport); }
        catch { _toasts.Show("Discord support couldn't open. Try again in a moment.", StatusTone.Warning); }
    }

    private void Send()
    {
        if (string.IsNullOrWhiteSpace(Message)) { _toasts.Show("Write a message first", StatusTone.Warning); return; }
        try
        {
            System.Windows.Clipboard.SetText(Message.Trim());
            ExternalLinks.Open(ExternalLinks.DiscordSupport);
            _toasts.Show("Request copied; Discord support opened", StatusTone.Success);
        }
        catch { _toasts.Show("Your request couldn't be opened. Your message is still here.", StatusTone.Warning); }
    }

    private async Task ExportDiagnosticAsync()
    {
        try
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Save Horizon diagnostic report",
                Filter = "Text report (*.txt)|*.txt",
                FileName = $"Horizon-diagnostics-{DateTime.Now:yyyyMMdd-HHmm}.txt",
                AddExtension = true,
                DefaultExt = ".txt"
            };
            if (dialog.ShowDialog() != true) return;
            var snapshot = await _systemInfo.ScanAsync();
            var lines = new[]
            {
                "Horizon diagnostic report", $"Created: {DateTimeOffset.Now:O}", $"Horizon: {typeof(SupportViewModel).Assembly.GetName().Version}",
                "", "System", $"Device: {snapshot.DeviceName}", $"Windows: {snapshot.WindowsEdition} ({snapshot.WindowsBuild})",
                $"CPU: {snapshot.Cpu}", $"GPU: {snapshot.Gpu} · driver {snapshot.GpuDriver}", $"Memory: {snapshot.Ram} · {snapshot.MemorySpeed}",
                $"Storage: {snapshot.Storage} · {snapshot.StorageFree} free", $"Network: {snapshot.NetworkAdapter}", $"Administrator: {(snapshot.IsAdministrator ? "Yes" : "No")}",
                "", "This report excludes account credentials, session tokens, personal files, and Horizon settings."
            };
            await File.WriteAllLinesAsync(dialog.FileName, lines);
            _toasts.Show("Diagnostic report saved", StatusTone.Success);
        }
        catch { _toasts.Show("Horizon couldn't save the diagnostic report.", StatusTone.Warning); }
    }
}

public sealed record AccountMethod(string Id, string Name, string Detail, bool IsLinked, string ActionLabel)
{
    public bool CanLink => !IsLinked && Id != "email";
}

public sealed class ProfileViewModel : PageViewModel
{
    private readonly IAuthenticationService _authentication;
    private readonly ISettingsService _settings;
    private readonly IToastService _toasts;
    private readonly IAppLogger _logger;
    private UserProfile _profile = new();
    private Func<Task> _signOut = () => Task.CompletedTask;
    private Action<UserProfile> _profileUpdated = _ => { };
    private string _statusMessage = string.Empty;
    private string _displayName = "Lachlan";
    private string? _availableProviderPhoto;

    public ProfileViewModel(IAuthenticationService authentication, ISettingsService settings, IToastService toasts, IAppLogger logger)
        : base("Account", "Your profile and sign-in methods.")
    {
        _authentication = authentication; _settings = settings; _toasts = toasts; _logger = logger;
        LinkProviderCommand = new AsyncRelayCommand(LinkProviderAsync);
        SaveProfileCommand = new AsyncRelayCommand(SaveProfileAsync, () => !string.IsNullOrWhiteSpace(DisplayName));
        UploadPhotoCommand = new AsyncRelayCommand(UploadPhotoAsync);
        UseProviderPhotoCommand = new AsyncRelayCommand(UseProviderPhotoAsync, () => CanUseProviderPhoto);
        RemovePhotoCommand = new AsyncRelayCommand(RemovePhotoAsync, () => HasProfilePhoto);
        SignOutCommand = new AsyncRelayCommand(() => _signOut());
    }
    public UserProfile Profile
    {
        get => _profile;
        private set
        {
            if (!Set(ref _profile, value)) return;
            RaiseProfileProperties();
        }
    }
    public string DisplayName
    {
        get => _displayName;
        set { if (Set(ref _displayName, value)) { Raise(nameof(Initials)); SaveProfileCommand.NotifyCanExecuteChanged(); } }
    }
    public string Initials => string.IsNullOrWhiteSpace(DisplayName) ? "L" : string.Concat(DisplayName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(part => part[0])).ToUpperInvariant();
    public string? ProfileImage => string.IsNullOrWhiteSpace(Profile.AvatarPath) ? Profile.ProviderAvatarUrl : Profile.AvatarPath;
    public bool HasProfilePhoto => !string.IsNullOrWhiteSpace(ProfileImage);
    public bool CanUseProviderPhoto => !string.IsNullOrWhiteSpace(_availableProviderPhoto) && !string.Equals(ProfileImage, _availableProviderPhoto, StringComparison.OrdinalIgnoreCase);
    public string PlanName => $"Horizon {Profile.Plan}";
    public string DeviceName => string.IsNullOrWhiteSpace(Profile.DeviceName) ? Environment.MachineName : Profile.DeviceName;
    public string EmailAddress => string.IsNullOrWhiteSpace(Profile.Email) ? "No email address" : Profile.Email;
    public IReadOnlyList<AccountMethod> AccountMethods
    {
        get
        {
            var linked = Profile.AuthenticationProviders.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var methods = new List<AccountMethod>();
            if (linked.Contains("email")) methods.Add(new("email", "Email", Profile.Email ?? "Email and password", true, "CONNECTED"));
            methods.Add(Method("google", "Google", linked));
            methods.Add(Method("discord", "Discord", linked));
            methods.Add(Method("epic", "Epic Games", linked));
            return methods;
        }
    }
    public string StatusMessage { get => _statusMessage; private set { if (Set(ref _statusMessage, value)) Raise(nameof(IsStatusVisible)); } }
    public bool IsStatusVisible => !string.IsNullOrWhiteSpace(StatusMessage);
    public AsyncRelayCommand LinkProviderCommand { get; }
    public AsyncRelayCommand SaveProfileCommand { get; }
    public AsyncRelayCommand UploadPhotoCommand { get; }
    public AsyncRelayCommand UseProviderPhotoCommand { get; }
    public AsyncRelayCommand RemovePhotoCommand { get; }
    public AsyncRelayCommand SignOutCommand { get; }
    public void SetProfile(UserProfile profile)
    {
        Profile = profile;
        DisplayName = profile.DisplayName;
        if (!string.IsNullOrWhiteSpace(profile.ProviderAvatarUrl)) _availableProviderPhoto = profile.ProviderAvatarUrl;
        RaiseProfileProperties();
    }
    public void SetSignOut(Func<Task> signOut) => _signOut = signOut;
    public void SetProfileUpdated(Action<UserProfile> profileUpdated) => _profileUpdated = profileUpdated;

    private async Task SaveProfileAsync()
    {
        StatusMessage = string.Empty;
        try
        {
            var localAvatar = Profile.AvatarPath;
            var updated = await _authentication.UpdateProfileAsync(DisplayName.Trim(), Profile.ProviderAvatarUrl);
            await ApplyUpdatedProfileAsync(updated, localAvatar);
            _toasts.Show("Profile saved", StatusTone.Success);
        }
        catch (Exception exception) { ShowProfileError(exception, "Horizon couldn't save your profile. Try again.", "auth.update-profile"); }
    }

    private async Task UploadPhotoAsync()
    {
        try
        {
            var path = ProfilePhotoPicker.ChooseAndStore();
            if (path is null) return;
            Profile.AvatarPath = path;
            await SaveLocalProfileAsync();
            _toasts.Show("Profile photo updated", StatusTone.Success);
        }
        catch (Exception exception) { ShowProfileError(exception, "Horizon couldn't use that photo.", "profile.photo.upload"); }
    }

    private async Task UseProviderPhotoAsync()
    {
        if (string.IsNullOrWhiteSpace(_availableProviderPhoto)) return;
        try
        {
            var updated = await _authentication.UpdateProfileAsync(DisplayName.Trim(), _availableProviderPhoto);
            _availableProviderPhoto = updated.ProviderAvatarUrl ?? _availableProviderPhoto;
            await ApplyUpdatedProfileAsync(updated, updated.ProviderAvatarUrl);
            _toasts.Show("Provider photo selected", StatusTone.Success);
        }
        catch (Exception exception) { ShowProfileError(exception, "Horizon couldn't use your provider photo.", "profile.photo.provider"); }
    }

    private async Task RemovePhotoAsync()
    {
        try
        {
            var updated = await _authentication.UpdateProfileAsync(DisplayName.Trim(), null);
            await ApplyUpdatedProfileAsync(updated, null);
            _toasts.Show("Profile photo removed", StatusTone.Success);
        }
        catch (Exception exception) { ShowProfileError(exception, "Horizon couldn't remove your photo.", "profile.photo.remove"); }
    }

    private async Task LinkProviderAsync(object? value)
    {
        var provider = value?.ToString() ?? "Google";
        StatusMessage = string.Empty;
        try
        {
            var result = await _authentication.LinkProviderAsync(provider);
            var localAvatar = Profile.AvatarPath;
            if (!string.IsNullOrWhiteSpace(result.Profile.ProviderAvatarUrl)) _availableProviderPhoto = result.Profile.ProviderAvatarUrl;
            await ApplyUpdatedProfileAsync(result.Profile, localAvatar);
            _toasts.Show($"{ProviderLabel(provider)} connected", StatusTone.Success);
        }
        catch (HorizonAuthenticationException exception)
        {
            StatusMessage = exception.Message;
            _toasts.Show(exception.Message, exception.State == AuthenticationState.Unavailable ? StatusTone.Warning : StatusTone.Danger);
            _logger.Error("auth.link-provider", exception, nameof(ProfileViewModel));
        }
        catch (Exception exception)
        {
            StatusMessage = "Horizon couldn't link that provider. Your account was not changed.";
            _toasts.Show(StatusMessage, StatusTone.Danger);
            _logger.Error("auth.link-provider", exception, nameof(ProfileViewModel));
        }
    }

    private async Task ApplyUpdatedProfileAsync(UserProfile updated, string? localAvatar)
    {
        updated.AvatarPath = localAvatar;
        updated.Plan = Profile.Plan;
        updated.DeviceName = DeviceName;
        Profile = updated;
        DisplayName = updated.DisplayName;
        await SaveLocalProfileAsync();
    }

    private async Task SaveLocalProfileAsync()
    {
        await _settings.SaveProfileAsync(Profile);
        RaiseProfileProperties();
        _profileUpdated(Profile);
    }

    private void ShowProfileError(Exception exception, string fallback, string operation)
    {
        StatusMessage = exception is HorizonAuthenticationException or ArgumentException ? exception.Message : fallback;
        _toasts.Show(StatusMessage, exception is HorizonAuthenticationException { State: AuthenticationState.Unavailable } ? StatusTone.Warning : StatusTone.Danger);
        _logger.Error(operation, exception, nameof(ProfileViewModel));
    }

    private void RaiseProfileProperties()
    {
        Raise(nameof(ProfileImage)); Raise(nameof(HasProfilePhoto)); Raise(nameof(CanUseProviderPhoto)); Raise(nameof(PlanName));
        Raise(nameof(DeviceName)); Raise(nameof(EmailAddress)); Raise(nameof(AccountMethods));
        UseProviderPhotoCommand.NotifyCanExecuteChanged(); RemovePhotoCommand.NotifyCanExecuteChanged();
    }

    private static AccountMethod Method(string id, string name, HashSet<string> linked) =>
        new(id, name, linked.Contains(id) ? "Connected to your account" : $"Connect {name} sign-in", linked.Contains(id), linked.Contains(id) ? "CONNECTED" : "CONNECT");
    private static string ProviderLabel(string value) => value.Trim().ToLowerInvariant() switch { "epic" or "epic games" => "Epic Games", "discord" => "Discord", "google" => "Google", "email" => "Email", _ => value };
}

internal static class ProfilePhotoPicker
{
    public static string? ChooseAndStore()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose a profile photo",
            Filter = "Image files|*.png;*.jpg;*.jpeg|PNG files|*.png|JPEG files|*.jpg;*.jpeg",
            Multiselect = false,
            CheckFileExists = true
        };
        if (dialog.ShowDialog() != true) return null;
        var file = new FileInfo(dialog.FileName);
        if (file.Length > 10 * 1024 * 1024) throw new InvalidOperationException("Choose an image smaller than 10 MB.");
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Horizon", "Profile");
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, $"avatar-{DateTime.UtcNow:yyyyMMddHHmmssfff}{file.Extension.ToLowerInvariant()}");
        File.Copy(file.FullName, destination, false);
        return destination;
    }
}
