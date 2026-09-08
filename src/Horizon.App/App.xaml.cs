using System.Windows;
using Horizon.App.ViewModels;
using Horizon.Core.Interfaces;
using Horizon.Core.Models;
using Horizon.Infrastructure.Persistence;
using Horizon.Restore;
using Horizon.Services;
using Horizon.Platform;
using Horizon.Tweaks;
using Microsoft.Extensions.DependencyInjection;

namespace Horizon.App;

public partial class App : Application
{
    private ServiceProvider? _services;
    private IAppLogger? _logger;
    private RootViewModel? _root;
    private string? _lastDispatcherError;
    private DateTimeOffset _lastDispatcherErrorAt;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            var collection = new ServiceCollection();
            ConfigureServices(collection);
            _services = collection.BuildServiceProvider();
            _logger = _services.GetRequiredService<IAppLogger>();
            RegisterExceptionLogging();

            _root = _services.GetRequiredService<RootViewModel>();
            var window = new MainWindow(_root, _services.GetRequiredService<IToastService>(), _services.GetRequiredService<IDialogService>());
            MainWindow = window;
            window.Show();
            _logger.Info("startup.window", "Horizon window opened while services initialize.", _root.CurrentPageName);

            // Paint the first frame before waiting on disk and network initialization. This
            // keeps a slow or unavailable authentication API from looking like an app hang.
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
            await _services.GetRequiredService<IHistoryService>().InitializeAsync();
            await _services.GetRequiredService<IGamingModeService>().StartAsync();
            await _root.InitializeAsync();
            _logger.Info("startup.complete", "Horizon finished initialization.", _root.CurrentPageName);
        }
        catch (Exception ex)
        {
            LogError("startup.failed", ex, "Startup");
            MessageBox.Show(
                "Horizon couldn't start. Please restart the app. If the problem continues, contact Horizon Support.",
                "Horizon startup error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _services?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        catch (Exception ex) { LogError("shutdown.services", ex); }
        base.OnExit(e);
    }

    private void RegisterExceptionLogging()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            var now = DateTimeOffset.UtcNow;
            var fingerprint = $"{args.Exception.GetType().FullName}|{args.Exception.Message}";
            if (!string.Equals(fingerprint, _lastDispatcherError, StringComparison.Ordinal) || now - _lastDispatcherErrorAt >= TimeSpan.FromMinutes(1))
            {
                _lastDispatcherError = fingerprint;
                _lastDispatcherErrorAt = now;
                LogError("dispatcher.unhandled", args.Exception);
                if (args.Exception is HorizonAuthenticationException)
                    _services?.GetService<IToastService>()?.Show("Authentication could not be completed. Horizon is still running.", StatusTone.Warning);
                else
                    _services?.GetService<IToastService>()?.Show("Something went wrong. No changes were made.", StatusTone.Danger);
            }
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception) LogError("app-domain.unhandled", exception);
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            LogError("task.unobserved", args.Exception);
            args.SetObserved();
        };
    }

    private void LogError(string operation, Exception exception, string? page = null) =>
        _logger?.Error(operation, exception, page ?? _root?.CurrentPageName ?? "Startup");

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<ISettingsService, JsonSettingsService>();
        services.AddSingleton<IHistoryService, SqliteHistoryService>();
        services.AddSingleton<IAppLogger, FileAppLogger>();
        services.AddSingleton<ISecureSessionStore, DpapiSessionStore>();
        services.AddSingleton<IAuthenticationService, BackendAuthenticationService>();
        services.AddSingleton<ISystemInfoService, WindowsSystemInfoService>();
        services.AddSingleton<ILaunchAtStartupService, WindowsLaunchAtStartupService>();
        services.AddSingleton<ICommandRunner, PowerShellCommandRunner>();
        services.AddSingleton<ITweakCatalog, HorizonTweakCatalog>();
        services.AddSingleton<ITweakOperationRegistry, TweakOperationRegistry>();
        services.AddSingleton<ICompatibilityScanner, CompatibilityScanner>();
        services.AddSingleton<ITweakEngine, TweakEngine>();
        services.AddSingleton<IRestoreService, RestoreService>();
        services.AddSingleton<ICleanupService, CleanupService>();
        services.AddSingleton<IDebloatService, DebloatService>();
        services.AddSingleton<IStartupService, StartupService>();
        services.AddSingleton<IGameDetectionService, GameDetectionService>();
        services.AddSingleton<IEntitlementService, LocalEntitlementService>();
        services.AddSingleton<IPurchaseService, PurchaseService>();
        services.AddSingleton<IUpdateService, UpdateService>();
        services.AddSingleton<IToastService, ToastService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IBenchmarkService, BenchmarkService>();
        services.AddSingleton<IActiveChangeStore, JsonActiveChangeStore>();
        services.AddSingleton<IGamingModeService, GamingModeService>();

        services.AddTransient<DashboardViewModel>(); services.AddTransient<RecommendationsViewModel>();
        services.AddTransient<TweaksViewModel>(); services.AddTransient<FortniteViewModel>();
        services.AddTransient<DebloatViewModel>(); services.AddTransient<StartupViewModel>();
        services.AddTransient<CleanupViewModel>(); services.AddTransient<SystemViewModel>();
        services.AddTransient<CompatibilityViewModel>(); services.AddTransient<BenchmarkViewModel>();
        services.AddTransient<RestoreViewModel>(); services.AddTransient<HistoryViewModel>();
        services.AddTransient<PlanViewModel>(); services.AddTransient<SettingsViewModel>();
        services.AddTransient<SupportViewModel>(); services.AddTransient<ShellViewModel>();
        services.AddTransient<ProfileViewModel>();
        services.AddSingleton<Func<ShellViewModel>>(provider => provider.GetRequiredService<ShellViewModel>);
        services.AddSingleton<RootViewModel>();
    }
}
