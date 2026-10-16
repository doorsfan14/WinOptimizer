using WinOptimizer.Models;
using WinOptimizer.Services;

namespace WinOptimizer.ViewModels;

public sealed class SetupWizardViewModel : ViewModelBase
{
    private readonly UserPreferences _preferences;
    private readonly SettingsStore _store;
    private readonly ITelemetryProvider _telemetry;
    private readonly StartupManager _startup;
    private readonly SafeCleanupService _cleanup;
    private readonly RecommendationEngine _recommendations;
    private readonly PowerPlanService _powerPlans;
    private readonly Action<SystemSnapshot> _applySnapshot;
    private readonly Action<IReadOnlyList<OptimizationRecommendation>> _applyRecommendations;
    private CancellationTokenSource? _scanCancellation;
    private int _stage;
    private bool _isBusy;
    private ThemeChoice _theme;
    private RecommendationLevel _level;
    private int _pollInterval;
    private string _status = "The scan is read-only and does not need administrator permission.";
    private string _findings = "System and sensor details will appear after discovery.";
    private SystemCapabilities? _capabilities;

    public int Stage { get => _stage; private set { if (Set(ref _stage, value)) { Raise(nameof(StepLabel)); Raise(nameof(Title)); Raise(nameof(Description)); Raise(nameof(NextLabel)); Raise(nameof(CanGoBack)); Raise(nameof(IsWelcome)); Raise(nameof(IsCompatibility)); Raise(nameof(IsPreferences)); Raise(nameof(IsComplete)); } } }
    public bool IsBusy { get => _isBusy; private set { if (Set(ref _isBusy, value)) { Raise(nameof(CanGoBack)); Raise(nameof(IsNextEnabled)); } } }
    public ThemeChoice Theme { get => _theme; set { if (Set(ref _theme, value)) { _preferences.Theme = value; ThemeService.Apply(value); } } }
    public RecommendationLevel Level { get => _level; set { if (Set(ref _level, value)) _preferences.RecommendationLevel = value; } }
    public int PollInterval { get => _pollInterval; set { if (Set(ref _pollInterval, value)) _preferences.PollIntervalSeconds = Math.Clamp(value, 2, 10); } }
    public IReadOnlyList<ThemeChoice> ThemeOptions { get; } = Enum.GetValues<ThemeChoice>();
    public IReadOnlyList<RecommendationLevel> LevelOptions { get; } = Enum.GetValues<RecommendationLevel>();
    public IReadOnlyList<int> PollOptions { get; } = [2, 5, 10];
    public string Status { get => _status; private set => Set(ref _status, value); }
    public string Findings { get => _findings; private set => Set(ref _findings, value); }
    public SystemCapabilities? Capabilities { get => _capabilities; private set { if (Set(ref _capabilities, value)) Raise(nameof(CompatibilitySummary)); } }
    public string CompatibilitySummary => Capabilities is null
        ? "Windows version, architecture, and permissions are checked during discovery."
        : $"{Capabilities.WindowsVersion} · {Capabilities.Architecture} · {Capabilities.LogicalProcessors} logical processors · {(Capabilities.IsAdministrator ? "Administrator session" : "Standard user; monitoring does not require elevation")}";
    public string StepLabel => $"STEP {Stage + 1} OF 4";
    public string Title => Stage switch
    {
        0 => "A clearer view of your PC.", 1 => "Let’s see what Windows exposes.",
        2 => "Make WinOptimizer yours.", _ => "Your first system overview is ready."
    };
    public string Description => Stage switch
    {
        0 => "WinOptimizer measures first, explains what it finds, and asks before changing anything. Your system data stays on this device.",
        1 => "Check Windows compatibility and discover supported hardware sensors. Missing readings are reported as unavailable, never guessed.",
        2 => "Safe recommendations are selected by default. Choose a theme and a monitoring interval that suits your PC.",
        _ => "Review the read-only scan and choose where you would like to start."
    };
    public string NextLabel => Stage switch { 0 => "Check compatibility", 1 => "Scan this PC", 2 => "Review findings", _ => "Open overview" };
    public bool CanGoBack => Stage > 0 && !IsBusy;
    public bool IsWelcome => Stage == 0;
    public bool IsCompatibility => Stage == 1;
    public bool IsPreferences => Stage == 2;
    public bool IsComplete => Stage == 3;
    public bool IsNextEnabled => !IsBusy;
    public bool SetupCompleted => _preferences.SetupCompleted;
    public RelayCommand BackCommand { get; }
    public AsyncRelayCommand NextCommand { get; }
    public AsyncRelayCommand SkipCommand { get; }
    public event Action? Dismissed;

    public SetupWizardViewModel(UserPreferences preferences, SettingsStore store, ITelemetryProvider telemetry,
        StartupManager startup, SafeCleanupService cleanup, RecommendationEngine recommendations,
        PowerPlanService powerPlans, Action<SystemSnapshot> applySnapshot, Action<IReadOnlyList<OptimizationRecommendation>> applyRecommendations)
    {
        _preferences = preferences; _store = store; _telemetry = telemetry; _startup = startup;
        _cleanup = cleanup; _recommendations = recommendations; _powerPlans = powerPlans; _applySnapshot = applySnapshot;
        _applyRecommendations = applyRecommendations; _theme = preferences.Theme; _level = preferences.RecommendationLevel; _pollInterval = preferences.PollIntervalSeconds;
        BackCommand = new RelayCommand(_ => Stage--, _ => CanGoBack);
        NextCommand = new AsyncRelayCommand(async _ => await NextAsync());
        SkipCommand = new AsyncRelayCommand(async _ => await SkipForNowAsync());
    }

    public void Begin() => Stage = 0;

    private async Task NextAsync()
    {
        if (Stage == 0) { Stage = 1; return; }
        if (Stage == 1) { await ScanAsync(); Stage = 2; return; }
        if (Stage == 2)
        {
            _preferences.Theme = Theme;
            _preferences.RecommendationLevel = Level;
            _preferences.PollIntervalSeconds = PollInterval;
            _preferences.SetupCompleted = true;
            await _store.SaveAsync(_preferences);
            Raise(nameof(SetupCompleted));
            Stage = 3;
            return;
        }
        Dismissed?.Invoke();
    }

    private async Task ScanAsync()
    {
        IsBusy = true;
        _scanCancellation?.Dispose();
        _scanCancellation = new CancellationTokenSource();
        var token = _scanCancellation.Token;
        try
        {
            Status = "Reading Windows version, architecture, and processor information…";
            Capabilities = await _telemetry.DetectCapabilitiesAsync(token);
            Status = "Sampling CPU, memory, storage, and sensors that Windows exposes…";
            var snapshot = await _telemetry.SampleAsync(token);
            _applySnapshot(snapshot);
            Status = "Reviewing current-user startup entries and older temporary files…";
            var startup = await _startup.ScanAsync(token);
            var cleanup = await _cleanup.PreviewAsync(token);
            var power = _powerPlans.ReadStatus();
            var findings = _recommendations.Evaluate(new RecommendationContext(snapshot,
                startup.Count(item => item.IsEnabled && item.Scope == "Current user"), cleanup.Candidates.Count, cleanup.TotalBytes, Level,
                power.HighPerformancePlanAvailable, power.IsOnAcPower, power.ActivePlanId));
            _applyRecommendations(findings);
            var cpu = Capabilities.CpuName ?? "Processor details unavailable";
            var graphics = Capabilities.GpuNames.Count == 0 ? "No graphics name exposed" : string.Join(", ", Capabilities.GpuNames);
            var fanStatus = snapshot.Fans.Count == 0 ? "fan RPM unavailable through the detected sensors" : $"{snapshot.Fans.Count} fan sensor(s) reporting";
            Findings = $"{cpu} · {graphics} · {MetricsMath.FormatBytes(cleanup.TotalBytes)} of older temporary files eligible for review · {fanStatus}. {findings.Count(item => item.State == RecommendationState.Applicable)} review item(s) found.";
            Status = Capabilities.SensorSummary;
        }
        catch (OperationCanceledException) { Status = "The scan was cancelled. You can continue with Windows measurements already available."; }
        catch (Exception ex)
        {
            Status = "Some checks were unavailable. The app can still use the measurements Windows provides.";
            Findings = "The initial scan did not complete every check. Supported measurements are available from the overview.";
            AppLog.Write("First-launch discovery was incomplete.", ex);
        }
        finally { IsBusy = false; }
    }

    public async Task SkipForNowAsync()
    {
        _scanCancellation?.Cancel();
        Level = RecommendationLevel.Safe;
        _preferences.SetupCompleted = true;
        try { await _store.SaveAsync(_preferences); }
        catch (Exception ex) { AppLog.Write("Setup defaults could not be saved.", ex); }
        Raise(nameof(SetupCompleted));
        Dismissed?.Invoke();
    }
}

