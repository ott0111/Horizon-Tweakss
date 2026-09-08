using Horizon.Core.Interfaces;
using Horizon.Core.Models;

namespace Horizon.Tweaks;

public sealed class HorizonTweakCatalog : ITweakCatalog
{
    private static readonly IReadOnlyList<TweakDefinition> Tweaks =
    [
        // Starter — Windows, gaming, cleanup, power, and safety.
        D("win.visual-effects", "Visual Effects Profile", "Windows · Interface", "Reduces non-essential animation while preserving accessibility.", "Uses the Windows performance visual-effects preset and records the previous value.", recommended: true),
        D("win.game-mode", "Windows Game Mode", "Windows · Gaming", "Helps Windows prioritise detected game workloads.", "Enables the supported Windows game workload scheduling mode.", recommended: true),
        D("win.game-dvr", "Disable Windows Game DVR", "Windows · Gaming", "Disables background gameplay recording when it is not required.", "Turns off the Game DVR background capture value.", recommended: true, restart: RestartRequirement.Application),
        D("win.app-capture", "Disable App Capture", "Windows · Gaming", "Disables the user-level app capture feature.", "Turns off Windows app capture and verifies the stored value.", restart: RestartRequirement.Application),
        D("win.background-apps", "Limit Background Apps", "Windows · Background", "Prevents supported Store apps from silently running in the background.", "Sets the supported per-user background-app policy.", restart: RestartRequirement.SignOut),
        D("win.notifications", "Disable Nonessential Notifications", "Windows · Notifications", "Turns off toast notifications for users who do not need them.", "Changes only the current user's notification value.", TweakRisk.Medium, restart: RestartRequirement.Explorer),
        D("win.taskbar-feeds", "Disable Taskbar Feeds", "Windows · Taskbar", "Removes background news and interests on Windows versions that expose it.", "Changes the taskbar feed value and can restore its prior existence/value.", restart: RestartRequirement.Explorer),
        D("win.menu-delay", "Improve Menu Responsiveness", "Windows · Responsiveness", "Reduces the Windows menu-open delay without removing accessibility features.", "Sets MenuShowDelay to a conservative responsive value.", recommended: true),
        D("win.process-scheduling", "Favor Foreground Programs", "Windows · CPU", "Uses the Windows interactive workload scheduling split.", "Sets Win32PrioritySeparation and verifies it after elevation.", TweakRisk.Medium, admin: true, restart: RestartRequirement.Windows),
        D("privacy.telemetry", "Required Diagnostics Only", "Windows · Privacy", "Uses the lowest generally available Windows diagnostic policy.", "Configures diagnostic collection without removing Windows components.", TweakRisk.Medium, admin: true, restart: RestartRequirement.Windows),
        D("input.mouse", "Disable Enhanced Pointer Precision", "Input · Mouse", "Keeps Windows pointer movement consistent for supported mice.", "Disables the Windows acceleration switch and records its exact prior value.", recommended: true, hardware: true, compatibility: "input"),
        D("startup.audit", "Startup Application Audit", "Cleanup · Startup", "Loads the real Run keys and Startup folders for individual control.", "Open Startup Manager to disable or restore detected items.", recommended: true, actionable: false),
        D("cleanup.temporary", "Temporary File Cleanup", "Cleanup · Files", "Scans known temporary and cache folders before removal.", "Open Cleanup to see measured sizes and choose categories.", recommended: true, temporary: true, actionable: false),
        D("debloat.audit", "Optional Application Audit", "Cleanup · Applications", "Lists current-user Windows application packages without a fake inventory.", "Open Debloat to choose individual removable packages.", actionable: false),
        D("win.features-review", "Optional Windows Features Review", "Windows · Features", "Reviews optional functionality without blindly disabling required Windows components.", "Provides a guided review rather than an unsafe generic removal.", actionable: false),
        D("power.plan-review", "Recommended Power Plan Review", "Power · Windows", "Reviews the active Windows power plan against the detected device.", "Performance plan automation is available in Performance; Starter still reports the actual plan.", hardware: true, actionable: false),
        D("storage.basic-review", "Storage Configuration Review", "Storage · Health", "Reports every detected physical drive and volume separately.", "Distinguishes SSD, HDD, bus type, health, capacity, and free space.", hardware: true, compatibility: "storage", actionable: false),
        D("gaming.launcher-review", "Game Launcher Settings Review", "Gaming · Launchers", "Shows launcher guidance only when supported software is detected.", "Reviews detected Epic and other game installation context.", compatibility: "games", actionable: false),
        D("gaming.discord-review", "Discord Gaming Settings Review", "Gaming · Discord", "Shows overlay and background guidance only when Discord is installed.", "Uses the detected Discord configuration path.", hardware: true, compatibility: "discord", actionable: false),
        D("safety.system-check", "Basic System Safety Check", "System · Safety", "Uses the live compatibility scan before any operation.", "Reports unsupported, locked, administrative, restart, and existing-value states.", recommended: true, actionable: false),

        // Performance — everything above plus implemented system operations and measured reviews.
        D("win.hags", "Hardware-accelerated GPU Scheduling", "GPU · Windows", "Uses Windows GPU scheduling on compatible drivers.", "Sets HwSchMode only when a GPU and supported Windows build are detected.", TweakRisk.Medium, admin: true, restart: RestartRequirement.Windows, hardware: true, plan: PlanTier.Performance, compatibility: "gpu"),
        D("perf.network-throttling", "Disable Multimedia Network Throttling", "Network · TCP/IP", "Removes the legacy multimedia network throttling limit.", "Sets NetworkThrottlingIndex and retains its exact previous state.", TweakRisk.Advanced, admin: true, restart: RestartRequirement.Windows, hardware: true, plan: PlanTier.Performance, compatibility: "network"),
        D("perf.system-responsiveness", "Multimedia System Responsiveness", "Windows · Scheduling", "Reduces the multimedia scheduler background reserve for gaming.", "Uses a conservative SystemResponsiveness value and verifies it.", TweakRisk.Advanced, admin: true, restart: RestartRequirement.Windows, plan: PlanTier.Performance),
        D("perf.games-priority", "Games Multimedia Priority", "Gaming · Scheduling", "Prioritises the Windows multimedia Games task.", "Sets the supported Games task priority value.", TweakRisk.Advanced, admin: true, restart: RestartRequirement.Windows, plan: PlanTier.Performance),
        D("net.autotuning", "TCP Receive-window Autotuning", "Network · TCP/IP", "Returns receive-window autotuning to the compatible normal level.", "Reads the current netsh value, avoids duplicate writes, and restores it exactly.", TweakRisk.Advanced, admin: true, restart: RestartRequirement.NetworkAdapter, hardware: true, plan: PlanTier.Performance, compatibility: "network"),
        D("power.throttling", "Disable System Power Throttling", "Power · Background", "Prevents supported workloads from being aggressively power throttled.", "Changes the Windows PowerThrottlingOff policy with an exact backup.", TweakRisk.Advanced, admin: true, restart: RestartRequirement.Windows, plan: PlanTier.Performance),
        D("power.performance", "High Performance Power Plan", "Power · Windows", "Activates the built-in performance-focused Windows plan.", "Records the active plan GUID and restores that exact plan.", TweakRisk.Medium, recommended: true, admin: true, hardware: true, plan: PlanTier.Performance),
        D("perf.disable-diagtrack", "Disable Connected User Experiences Service", "Windows · Services", "Disables the optional diagnostic service where it exists.", "Records startup and running state before applying the service change.", TweakRisk.Medium, admin: true, plan: PlanTier.Performance, compatibility: "diagtrack"),
        D("perf.cpu-review", "CPU Performance and Thermal Review", "CPU · Hardware", "Reports the detected CPU, core/thread configuration, and maximum clock context.", "Enables CPU-specific guidance only after the processor is detected.", hardware: true, plan: PlanTier.Performance, compatibility: "cpu", actionable: false),
        D("perf.nvidia-review", "NVIDIA Driver Settings Review", "GPU · NVIDIA", "Keeps NVIDIA-only guidance separate from AMD hardware.", "Reports the detected NVIDIA model and driver before vendor-specific guidance.", hardware: true, plan: PlanTier.Performance, compatibility: "nvidia", actionable: false),
        D("perf.amd-review", "AMD Driver Settings Review", "GPU · AMD", "Keeps AMD-only guidance separate from NVIDIA hardware.", "Reports the detected AMD model and driver before vendor-specific guidance.", hardware: true, plan: PlanTier.Performance, compatibility: "amd", actionable: false),
        D("perf.ram-review", "RAM Configuration Review", "Memory · Hardware", "Reports every memory module and configured speed.", "Surfaces module capacity and speed evidence for XMP/EXPO/DOCP review.", hardware: true, plan: PlanTier.Performance, compatibility: "memory", actionable: false),
        D("perf.storage-health", "Per-drive Storage Health Review", "Storage · Health", "Reports SSD/HDD type and health for each detected physical drive.", "Does not treat HDDs and SSDs as identical hardware.", hardware: true, plan: PlanTier.Performance, compatibility: "storage", actionable: false),
        D("perf.network-adapter", "Active Network Adapter Review", "Network · Adapter", "Reports the active Ethernet or Wi-Fi adapter and driver.", "Adapter-specific recommendations are shown only when an active interface is detected.", hardware: true, plan: PlanTier.Performance, compatibility: "network", actionable: false),
        D("perf.input-devices", "Connected Input Device Review", "Input · Devices", "Lists detected mouse, keyboard, HID, and controller devices.", "Provides evidence before USB or polling-rate guidance.", hardware: true, plan: PlanTier.Performance, compatibility: "input", actionable: false),
        D("perf.latency-test", "Ping, Jitter, and Stability Test", "Testing · Network", "Runs a measured network diagnostic from the Benchmark page.", "Never populates latency with a fabricated result.", hardware: true, plan: PlanTier.Performance, compatibility: "network", actionable: false),
        D("perf.pagefile-review", "Virtual Memory Review", "Memory · Windows", "Reviews the currently detected memory before page-file guidance.", "Does not apply a generic page-file size to every PC.", hardware: true, plan: PlanTier.Performance, compatibility: "memory", actionable: false),
        D("perf.existing-overlap", "Existing Optimization Overlap Review", "System · Conflicts", "Flags current values that overlap or drift from Horizon-managed changes.", "Uses the active-change journal to detect update or application changes.", plan: PlanTier.Performance, actionable: false),

        // Fortnite receives the first complete automatic game configuration.
        D("fortnite.vsync", "Fortnite: Disable VSync", "Fortnite · Configuration", "Backs up GameUserSettings.ini before disabling in-game VSync.", "Changes only bUseVSync in the detected Fortnite settings section.", TweakRisk.Medium, restart: RestartRequirement.Application, hardware: true, plan: PlanTier.Performance, entitlement: "game-fortnite", compatibility: "fortnite"),
        D("fortnite.grass", "Fortnite: Disable Grass", "Fortnite · Competitive", "Backs up the configuration before applying the competitive grass setting.", "Changes only bShowGrass and preserves unrelated game settings.", TweakRisk.Medium, restart: RestartRequirement.Application, hardware: true, plan: PlanTier.Performance, entitlement: "game-fortnite", compatibility: "fortnite"),
        D("fortnite.temporary-power", "Fortnite Temporary Performance Plan", "Fortnite · Temporary", "Uses the performance plan only while Fortnite is running.", "Gaming Mode restores the exact previous plan after the process closes.", TweakRisk.Medium, temporary: true, hardware: true, plan: PlanTier.Performance, entitlement: "game-fortnite", compatibility: "fortnite"),
        D("fortnite.temporary-dvr", "Fortnite Temporary Capture Suppression", "Fortnite · Temporary", "Suppresses Game DVR only for the Fortnite session.", "Gaming Mode restores the exact previous value after Fortnite closes.", TweakRisk.Medium, restart: RestartRequirement.Application, temporary: true, hardware: true, plan: PlanTier.Performance, entitlement: "game-fortnite", compatibility: "fortnite"),

        // Ultimate and separately unlockable advanced workflows.
        D("ultimate.bios-review", "Motherboard and BIOS Configuration Review", "BIOS · Motherboard", "Builds guidance from the detected motherboard, BIOS, CPU platform, and memory.", "Firmware writes remain guided because Windows cannot safely apply vendor firmware settings generically.", TweakRisk.Advanced, hardware: true, plan: PlanTier.Ultimate, entitlement: "bios", compatibility: "bios", actionable: false),
        D("ultimate.xmp-review", "XMP / EXPO / DOCP Review", "Memory · Firmware", "Compares detected module and configured memory speeds.", "Presents the appropriate memory-profile workflow and requires stability testing.", TweakRisk.Advanced, hardware: true, plan: PlanTier.Ultimate, entitlement: "ram", compatibility: "memory", actionable: false),
        D("ultimate.rebar-review", "Resizable BAR / SAM Review", "BIOS · GPU", "Uses detected GPU and firmware context for ReBAR/SAM guidance.", "Keeps Above 4G Decoding and vendor-specific requirements explicit.", TweakRisk.Advanced, hardware: true, plan: PlanTier.Ultimate, entitlement: "bios", compatibility: "gpu", actionable: false),
        D("ultimate.cpu-tuning", "Advanced CPU Power and Thermal Workflow", "CPU · Advanced", "Provides hardware-specific power-limit, undervolt, and balance guidance.", "Unsupported voltage or firmware writes are never issued generically.", TweakRisk.Advanced, hardware: true, plan: PlanTier.Ultimate, entitlement: "cpu", compatibility: "cpu", actionable: false),
        D("ultimate.gpu-tuning", "Advanced GPU Clock and Voltage Workflow", "GPU · Advanced", "Keeps NVIDIA and AMD tuning paths separate.", "Requires supported vendor tooling before clock, voltage, or power-limit controls are enabled.", TweakRisk.Advanced, hardware: true, plan: PlanTier.Ultimate, entitlement: "gpu", compatibility: "gpu", actionable: false),
        D("ultimate.network", "Full Network Diagnostic and QoS Review", "Network · Advanced", "Combines adapter, DNS, packet-loss, ping, jitter, and router guidance.", "Shows measured test results and hardware-specific recommendations.", TweakRisk.Advanced, hardware: true, plan: PlanTier.Ultimate, entitlement: "network", compatibility: "network", actionable: false),
        D("ultimate.custom-debloat", "Custom Application, Service, and Task Audit", "Cleanup · Advanced", "Combines real application, service, startup, telemetry, and scheduled-task inventories.", "Requires user selection and preserves protected Windows functionality.", TweakRisk.Advanced, plan: PlanTier.Ultimate, actionable: false),
        D("ultimate.pc-checkup", "PC Health and Bottleneck Checkup", "Testing · Checkup", "Combines live Windows, memory, storage, and diagnostic evidence.", "Produces no synthetic health or performance score.", TweakRisk.Advanced, hardware: true, plan: PlanTier.Ultimate, entitlement: "pc-checkup", actionable: false),
        D("ultimate.stream", "OBS Stream Setup Workflow", "Streaming · OBS", "Reviews detected OBS encoder, bitrate, audio, and performance context.", "Shown only when an OBS configuration is detected.", TweakRisk.Advanced, hardware: true, plan: PlanTier.Ultimate, entitlement: "stream", compatibility: "obs", actionable: false),
        D("game.valorant", "Valorant Configuration Review", "Games · Valorant", "Detects Valorant before showing game-specific config and driver guidance.", "Supports a separately unlockable per-game workflow.", TweakRisk.Advanced, hardware: true, plan: PlanTier.Ultimate, entitlement: "game-valorant", compatibility: "game:valorant", actionable: false),
        D("game.call-of-duty", "Call of Duty Configuration Review", "Games · Call of Duty", "Detects Call of Duty before showing profile guidance.", "Keeps launch, config, and GPU paths game-specific.", TweakRisk.Advanced, hardware: true, plan: PlanTier.Ultimate, entitlement: "game-callOfDuty", compatibility: "game:callOfDuty", actionable: false),
        D("game.apex", "Apex Legends Configuration Review", "Games · Apex Legends", "Detects Apex Legends before showing profile guidance.", "Keeps launch, config, and GPU paths game-specific.", TweakRisk.Advanced, hardware: true, plan: PlanTier.Ultimate, entitlement: "game-apex", compatibility: "game:apex", actionable: false),
        D("game.cs2", "Counter-Strike 2 Configuration Review", "Games · CS2", "Detects Counter-Strike 2 before showing profile guidance.", "Keeps launch, config, and GPU paths game-specific.", TweakRisk.Advanced, hardware: true, plan: PlanTier.Ultimate, entitlement: "game-cs2", compatibility: "game:cs2", actionable: false),
        D("game.rocket-league", "Rocket League Configuration Review", "Games · Rocket League", "Detects Rocket League before showing profile guidance.", "Keeps launch, config, and GPU paths game-specific.", TweakRisk.Advanced, hardware: true, plan: PlanTier.Ultimate, entitlement: "game-rocketLeague", compatibility: "game:rocketLeague", actionable: false)
    ];

    public IReadOnlyList<TweakDefinition> GetAll() => Tweaks;

    private static TweakDefinition D(string id, string name, string category, string description, string whatItDoes,
        TweakRisk risk = TweakRisk.Low, bool recommended = false, bool admin = false, RestartRequirement restart = RestartRequirement.None,
        bool temporary = false, bool hardware = false, PlanTier plan = PlanTier.Starter, string? entitlement = null,
        string compatibility = "windows", bool actionable = true) =>
        new(id, name, category, description, whatItDoes, risk, recommended, admin, restart, temporary, hardware, plan, entitlement, compatibility, actionable);
}

public sealed class CompatibilityScanner : ICompatibilityScanner
{
    private readonly ISystemInfoService _systemInfo;
    private readonly ITweakOperationRegistry _operations;
    private readonly IActiveChangeStore _activeChanges;
    private readonly IEntitlementService _entitlements;

    public CompatibilityScanner(ISystemInfoService systemInfo, ITweakOperationRegistry operations, IActiveChangeStore activeChanges, IEntitlementService entitlements)
    {
        _systemInfo = systemInfo; _operations = operations; _activeChanges = activeChanges; _entitlements = entitlements;
    }

    public async Task<IReadOnlyDictionary<string, TweakStatus>> ScanAsync(IEnumerable<TweakDefinition> tweaks, CancellationToken cancellationToken = default)
    {
        var system = await _systemInfo.ScanAsync(cancellationToken);
        var active = await _activeChanges.GetAllAsync(cancellationToken);
        var plan = await _entitlements.GetPlanAsync(cancellationToken);
        var statuses = new Dictionary<string, TweakStatus>(StringComparer.OrdinalIgnoreCase);
        foreach (var tweak in tweaks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var compatibility = CheckMetadata(tweak, system);
            if (compatibility.Supported && _operations.TryGet(tweak.Id, out var compatibleOperation))
                compatibility = await compatibleOperation.CheckCompatibilityAsync(system, cancellationToken);
            if (!compatibility.Supported)
            {
                statuses[tweak.Id] = new(tweak.Id, false, TweakApplyState.Unsupported, "Unavailable", "Unavailable", compatibility.Reason);
                continue;
            }
            var entitled = plan >= tweak.RequiredPlan || tweak.RequiredEntitlement is not null && await _entitlements.HasEntitlementAsync(tweak.RequiredEntitlement, cancellationToken);
            if (!entitled)
            {
                statuses[tweak.Id] = new(tweak.Id, true, TweakApplyState.Locked, CurrentReviewValue(tweak, system), RequiredAccess(tweak), $"Requires {tweak.RequiredEntitlement ?? tweak.RequiredPlan.ToString()} access.");
                continue;
            }
            if (!_operations.TryGet(tweak.Id, out var operation))
            {
                statuses[tweak.Id] = new(tweak.Id, true, TweakApplyState.Available, CurrentReviewValue(tweak, system), "Guided review", null, false, false);
                continue;
            }
            try
            {
                var current = await operation.ReadAsync(system, cancellationToken);
                var desired = operation.IsDesired(current);
                var hasActive = active.TryGetValue(tweak.Id, out _);
                var state = hasActive && !desired ? TweakApplyState.UpdateChanged
                    : hasActive && desired ? TweakApplyState.Applied
                    : desired ? TweakApplyState.AlreadyApplied
                    : tweak.RequiresAdmin && !system.IsAdministrator ? TweakApplyState.AdminRequired
                    : TweakApplyState.Available;
                statuses[tweak.Id] = new(tweak.Id, true, state, current.Display, operation.DesiredDisplay,
                    state == TweakApplyState.UpdateChanged ? "Windows or another application changed this value after Horizon applied it." : null,
                    desired, hasActive);
            }
            catch (Exception exception)
            {
                statuses[tweak.Id] = new(tweak.Id, true, TweakApplyState.Failed, "Read failed", operation.DesiredDisplay, exception.Message, false, active.ContainsKey(tweak.Id), exception.Message);
            }
        }
        return statuses;
    }

    internal static OperationCompatibility CheckMetadata(TweakDefinition tweak, SystemSnapshot system)
    {
        if (!system.PlatformSupported || !OperatingSystem.IsWindows()) return new(false, "Requires Windows 10 or Windows 11.");
        var games = system.InstalledGames ?? new Dictionary<string, string>();
        var software = system.DetectedSoftware ?? new Dictionary<string, string>();
        var supported = tweak.Compatibility switch
        {
            "windows" => true,
            "cpu" => system.CpuCores > 0 && !system.Cpu.StartsWith("Detecting", StringComparison.OrdinalIgnoreCase),
            "gpu" => system.Gpus?.Count > 0,
            "nvidia" => system.Gpus?.Any(gpu => gpu.Vendor.Equals("nvidia", StringComparison.OrdinalIgnoreCase)) == true,
            "amd" => system.Gpus?.Any(gpu => gpu.Vendor.Equals("amd", StringComparison.OrdinalIgnoreCase)) == true,
            "memory" => system.MemoryModules?.Count > 0,
            "storage" => system.Drives?.Count > 0,
            "network" => system.NetworkAdapters?.Count > 0,
            "input" => system.InputDevices?.Count > 0,
            "games" => games.Count > 0,
            "fortnite" => games.ContainsKey("fortnite"),
            "discord" => software.ContainsKey("discord"),
            "obs" => software.ContainsKey("obs"),
            "bios" => !string.IsNullOrWhiteSpace(system.Motherboard) && system.Motherboard != "Not detected",
            "diagtrack" => true,
            _ when tweak.Compatibility.StartsWith("game:", StringComparison.Ordinal) => games.ContainsKey(tweak.Compatibility[5..]),
            _ => true
        };
        return supported ? new(true) : new(false, $"Required {tweak.Compatibility.Replace("game:", string.Empty)} hardware or software was not detected.");
    }

    private static string RequiredAccess(TweakDefinition tweak) => tweak.RequiredEntitlement is not null ? $"Unlock {tweak.RequiredEntitlement}" : $"Unlock {tweak.RequiredPlan}";
    private static string CurrentReviewValue(TweakDefinition tweak, SystemSnapshot system) => tweak.Compatibility switch
    {
        "cpu" => system.Cpu,
        "gpu" or "nvidia" or "amd" => system.Gpu,
        "memory" => $"{system.Ram} · {system.MemorySpeed}",
        "storage" => system.Storage,
        "network" => system.NetworkAdapter,
        "fortnite" => system.InstalledGames?.GetValueOrDefault("fortnite") ?? "Not detected",
        "bios" => $"{system.Motherboard} · {system.Bios}",
        _ => "Detected"
    };
}
