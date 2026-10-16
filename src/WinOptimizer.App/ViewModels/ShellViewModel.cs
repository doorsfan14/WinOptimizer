using System.Collections.ObjectModel;
using System.Windows;
using WinOptimizer.Models;
using WinOptimizer.Services;

namespace WinOptimizer.ViewModels;

public sealed class NavigationItem : ViewModelBase
{
    private bool _isSelected;
    public string Key { get; }
    public string Label { get; }
    public string Glyph { get; }
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
    public NavigationItem(string key, string label, string glyph) { Key = key; Label = label; Glyph = glyph; }
}

public sealed class MainViewModel : ViewModelBase, IDisposable
{
    private readonly ITelemetryProvider _telemetry;
    private readonly UserPreferences _preferences;
    private readonly StartupManager _startupManager = new();
    private readonly SafeCleanupService _cleanup = new();
    private readonly ActivityLog _activity = new();
    private readonly RecommendationEngine _recommendations = new();
    private CancellationTokenSource? _monitorCancellation;
    private Task? _monitorTask;
    private bool _windowVisible = true;
    private SystemSnapshot _snapshot = SystemSnapshot.Empty;
    private PageViewModel _currentPage;
    private NavigationItem? _selectedNavigation;

    public ObservableCollection<NavigationItem> Navigation { get; } =
    [
        new("Overview", "Overview", "\uE80F"), new("Hardware", "Hardware", "\uE950"),
        new("Performance", "Performance", "\uE9D9"), new("Optimize", "Optimize", "\uE7BA"),
        new("Startup", "Startup", "\uE748"), new("Storage", "Storage", "\uEDA2"),
        new("History", "History", "\uE81C")
    ];
    public OverviewViewModel Overview { get; }
    public HardwareViewModel Hardware { get; } = new();
    public PerformanceViewModel Performance { get; }
    public StartupViewModel Startup { get; }
    public StorageViewModel Storage { get; }
    public OptimizeViewModel Optimize { get; }
    public HistoryViewModel History { get; }
    public SettingsViewModel Settings { get; }
    public SetupWizardViewModel SetupWizard { get; }
    public SystemSnapshot Snapshot { get => _snapshot; private set { if (Set(ref _snapshot, value)) { Raise(nameof(MonitoringStatus)); Raise(nameof(LastUpdateText)); } } }
    public PageViewModel CurrentPage { get => _currentPage; private set => Set(ref _currentPage, value); }
    public NavigationItem? SelectedNavigation { get => _selectedNavigation; private set => Set(ref _selectedNavigation, value); }
    public string MonitoringStatus => Snapshot.SensorState == ReadingState.Error ? "Some hardware sensors could not be read" : "Monitoring on this device";
    public string LastUpdateText => Snapshot.SensorState == ReadingState.NotInitialized ? "Waiting for first sample" : $"Last sample {Snapshot.SampledAt:HH:mm:ss}";
    public RelayCommand NavigateCommand { get; }
    public RelayCommand OpenSettingsCommand { get; }
    public RelayCommand RefreshCommand { get; }
    public event Action? SetupRequested;

    public MainViewModel(ITelemetryProvider telemetry, SettingsStore settingsStore, UserPreferences preferences)
    {
        _telemetry = telemetry; _preferences = preferences;
        Overview = new OverviewViewModel(Math.Clamp(preferences.HistoryDurationMinutes * 60 / Math.Max(1, preferences.PollIntervalSeconds), 30, 3600), preferences.HistoryDurationMinutes);
        Performance = new PerformanceViewModel(() => _telemetry.GetProcessesAsync(), Overview.CpuHistory, Overview.MemoryHistory);
        Startup = new StartupViewModel(_startupManager, _activity, ReloadRecommendationsAsync);
        Storage = new StorageViewModel(_cleanup, _activity, RefreshNow);
        Optimize = new OptimizeViewModel(ResolveRecommendationAction);
        History = new HistoryViewModel(_activity, _startupManager);
        Settings = new SettingsViewModel(preferences, settingsStore, OpenSetupWizard);
        Settings.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(SettingsViewModel.HistoryDuration) or nameof(SettingsViewModel.PollInterval))
                Overview.SetHistoryLimit(Math.Clamp(_preferences.HistoryDurationMinutes * 60 / Math.Max(1, _preferences.PollIntervalSeconds), 30, 3600), _preferences.HistoryDurationMinutes);
            if (args.PropertyName == nameof(SettingsViewModel.Level) && CurrentPage == Optimize)
                _ = ScanRecommendationsAsync();
        };
        SetupWizard = new SetupWizardViewModel(preferences, settingsStore, _telemetry, _startupManager, _cleanup,
            _recommendations, ApplySnapshot, ApplyRecommendations);
        _currentPage = Overview; _selectedNavigation = Navigation[0];
        NavigateCommand = new RelayCommand(Navigate);
        OpenSettingsCommand = new RelayCommand(_ => Navigate("Settings"));
        RefreshCommand = new RelayCommand(_ => RefreshNow());
    }

    public async Task StartMonitoringAsync()
    {
        if (_monitorTask is not null) return;
        _monitorCancellation = new CancellationTokenSource();
        var token = _monitorCancellation.Token;
        await RefreshSnapshotAsync(token);
        _monitorTask = Task.Run(() => MonitorLoopAsync(token), token);
    }

    private async Task MonitorLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var seconds = _windowVisible ? Math.Clamp(_preferences.PollIntervalSeconds, 1, 10) : Math.Max(10, _preferences.PollIntervalSeconds * 5);
            try { await Task.Delay(TimeSpan.FromSeconds(seconds), token); }
            catch (OperationCanceledException) { break; }
            await RefreshSnapshotAsync(token);
            if (_windowVisible && CurrentPage == Performance)
            {
                var rows = await _telemetry.GetProcessesAsync(token);
                await Application.Current.Dispatcher.InvokeAsync(() => Performance.UpdateProcesses(rows));
            }
        }
    }

    private async Task RefreshSnapshotAsync(CancellationToken token = default)
    {
        try
        {
            var snapshot = await _telemetry.SampleAsync(token);
            await Application.Current.Dispatcher.InvokeAsync(() => ApplySnapshot(snapshot));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { AppLog.Write("A Windows telemetry sample failed.", ex); }
    }

    private void ApplySnapshot(SystemSnapshot snapshot)
    {
        Snapshot = snapshot; Overview.Update(snapshot); Hardware.Update(snapshot); Performance.UpdateSnapshot(snapshot); Storage.Update(snapshot);
    }

    private async void Navigate(object? parameter)
    {
        if (parameter is not string key) return;
        foreach (var item in Navigation) item.IsSelected = item.Key == key;
        SelectedNavigation = Navigation.FirstOrDefault(item => item.Key == key);
        CurrentPage = key switch
        {
            "Hardware" => Hardware, "Performance" => Performance, "Optimize" => Optimize,
            "Startup" => Startup, "Storage" => Storage, "History" => History,
            "Settings" => Settings, _ => Overview
        };
        if (CurrentPage == Startup && Startup.Items.Count == 0) await Startup.RefreshAsync();
        if (CurrentPage == Performance) await Performance.RefreshAsync();
        if (CurrentPage == History) await History.RefreshAsync();
        if (CurrentPage == Optimize) await ScanRecommendationsAsync();
    }

    private async Task ScanRecommendationsAsync()
    {
        Optimize.Status = "Reading startup settings and the eligible temporary-file list…";
        try
        {
            var startup = await _startupManager.ScanAsync();
            var preview = await _cleanup.PreviewAsync();
            ApplyRecommendations(_recommendations.Evaluate(new RecommendationContext(
                Snapshot, startup.Count(item => item.IsEnabled && item.Scope == "Current user"),
                preview.Candidates.Count, preview.TotalBytes, _preferences.RecommendationLevel)));
        }
        catch (Exception ex)
        {
            Optimize.Status = "The read-only scan was incomplete. No system settings were changed.";
            AppLog.Write("Recommendation scan failed.", ex);
        }
    }

    private async Task ReloadRecommendationsAsync()
    {
        if (Startup.Items.Count == 0) return;
        var preview = await _cleanup.PreviewAsync();
        ApplyRecommendations(_recommendations.Evaluate(new RecommendationContext(Snapshot,
            Startup.Items.Count(item => item.IsEnabled && item.Scope == "Current user"), preview.Candidates.Count, preview.TotalBytes, _preferences.RecommendationLevel)));
    }

    private void ApplyRecommendations(IReadOnlyList<OptimizationRecommendation> items) => Optimize.Update(items);

    private void ResolveRecommendationAction(string id)
    {
        Navigate(id switch
        {
            "storage.low-space" => "Storage",
            "startup.review" => "Startup",
            "memory.pressure" => "Performance",
            _ => "Overview"
        });
    }

    private void RefreshNow() => _ = RefreshSnapshotAsync(_monitorCancellation?.Token ?? CancellationToken.None);
    public void SetWindowVisible(bool visible) => _windowVisible = visible;
    public void OpenSetupWizard()
    {
        SetupWizard.Begin();
        SetupRequested?.Invoke();
    }

    public void RefreshPreferenceEffects() =>
        Overview.SetHistoryLimit(Math.Clamp(_preferences.HistoryDurationMinutes * 60 / Math.Max(1, _preferences.PollIntervalSeconds), 30, 3600), _preferences.HistoryDurationMinutes);

    public void Dispose()
    {
        _monitorCancellation?.Cancel();
        try { _monitorTask?.Wait(TimeSpan.FromSeconds(2)); } catch { }
        _monitorCancellation?.Dispose();
        _telemetry.Dispose();
    }
}
