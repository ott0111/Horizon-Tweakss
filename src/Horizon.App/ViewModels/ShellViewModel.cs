using System.Collections.ObjectModel;
using Horizon.Core.Models;

namespace Horizon.App.ViewModels;

public sealed class NavigationItemViewModel
{
    public NavigationItemViewModel(NavigationDestination destination, string label, string icon) { Destination = destination; Label = label; Icon = MaterialIconConverter.Glyph(icon); }
    public NavigationDestination Destination { get; }
    public string Label { get; }
    public string Icon { get; }
}

public sealed class NavigationGroupViewModel
{
    public NavigationGroupViewModel(string title, params NavigationItemViewModel[] items) { Title = title; Items = items; }
    public string Title { get; }
    public IReadOnlyList<NavigationItemViewModel> Items { get; }
}

public sealed class ShellViewModel : ObservableObject
{
    private readonly Dictionary<NavigationDestination, PageViewModel> _pages;
    private PageViewModel _currentPage;
    private NavigationDestination _selectedDestination;
    private UserProfile _profile = new();

    public ShellViewModel(
        DashboardViewModel dashboard, RecommendationsViewModel recommendations, TweaksViewModel tweaks,
        FortniteViewModel fortnite, DebloatViewModel debloat, StartupViewModel startup, CleanupViewModel cleanup,
        SystemViewModel system, CompatibilityViewModel compatibility, BenchmarkViewModel benchmark,
        RestoreViewModel restore, HistoryViewModel history, PlanViewModel plan, SettingsViewModel settings,
        SupportViewModel support, ProfileViewModel profile)
    {
        _pages = new()
        {
            [NavigationDestination.Dashboard] = dashboard, [NavigationDestination.Recommended] = recommendations,
            [NavigationDestination.AllTweaks] = tweaks, [NavigationDestination.Fortnite] = fortnite,
            [NavigationDestination.Debloat] = debloat, [NavigationDestination.Startup] = startup,
            [NavigationDestination.Cleanup] = cleanup, [NavigationDestination.System] = system,
            [NavigationDestination.Compatibility] = compatibility, [NavigationDestination.Benchmark] = benchmark,
            [NavigationDestination.Restore] = restore, [NavigationDestination.History] = history,
            [NavigationDestination.Profile] = profile,
            [NavigationDestination.Plan] = plan, [NavigationDestination.Settings] = settings,
            [NavigationDestination.Support] = support
        };
        _currentPage = dashboard;
        NavigateCommand = new RelayCommand(BeginNavigate);
        dashboard.SetNavigate(destination => NavigateAsync(destination));
        profile.SetProfileUpdated(updated =>
        {
            if (ReferenceEquals(Profile, updated)) Raise(nameof(Profile)); else Profile = updated;
            dashboard.SetProfile(updated);
        });
        Groups =
        [
            new("HOME", new NavigationItemViewModel(NavigationDestination.Dashboard, "Dashboard", "home")),
            new("OPTIMIZE", new(NavigationDestination.Recommended, "Recommended", "auto_awesome"), new(NavigationDestination.AllTweaks, "All Tweaks", "tune"), new(NavigationDestination.Fortnite, "Fortnite", "sports_esports"), new(NavigationDestination.Debloat, "Debloat", "delete_sweep"), new(NavigationDestination.Startup, "Startup", "rocket_launch"), new(NavigationDestination.Cleanup, "Cleanup", "cleaning_services")),
            new("SYSTEM", new(NavigationDestination.System, "System", "memory"), new(NavigationDestination.Compatibility, "Compatibility", "verified_user"), new(NavigationDestination.Benchmark, "Benchmark", "speed"), new(NavigationDestination.Restore, "Restore", "restore"), new(NavigationDestination.History, "History", "history")),
            new("ACCOUNT", new(NavigationDestination.Plan, "Plan", "workspace_premium"), new(NavigationDestination.Settings, "Settings", "settings"), new(NavigationDestination.Support, "Support", "help_outline"))
        ];
    }

    public IReadOnlyList<NavigationGroupViewModel> Groups { get; }
    public PageViewModel CurrentPage { get => _currentPage; private set => Set(ref _currentPage, value); }
    public NavigationDestination SelectedDestination { get => _selectedDestination; private set => Set(ref _selectedDestination, value); }
    public UserProfile Profile { get => _profile; private set => Set(ref _profile, value); }
    public RelayCommand NavigateCommand { get; }

    public void SetProfile(UserProfile profile)
    {
        Profile = profile;
        if (_pages[NavigationDestination.Dashboard] is DashboardViewModel dashboard) dashboard.SetProfile(profile);
        if (_pages[NavigationDestination.Profile] is ProfileViewModel page) page.SetProfile(profile);
    }
    public void SetSignOut(Func<Task> signOut)
    {
        if (_pages[NavigationDestination.Profile] is ProfileViewModel page) page.SetSignOut(signOut);
    }
    public void SetSettingsUpdated(Action<AppSettings> settingsUpdated)
    {
        if (_pages[NavigationDestination.Settings] is SettingsViewModel page) page.SetSettingsUpdated(settingsUpdated);
    }
    public Task InitializeAsync() => CurrentPage.LoadAsync();

    private async void BeginNavigate(object? parameter)
    {
        try { await NavigateAsync(parameter); }
        catch { /* Page failures stay contained by their own state and must never terminate navigation. */ }
    }

    private async Task NavigateAsync(object? parameter)
    {
        if (parameter is NavigationDestination destination || Enum.TryParse(parameter?.ToString(), out destination))
        {
            if (destination == SelectedDestination && ReferenceEquals(CurrentPage, _pages[destination])) return;
            SelectedDestination = destination;
            CurrentPage = _pages[destination];
            await CurrentPage.LoadAsync();
        }
    }
}
