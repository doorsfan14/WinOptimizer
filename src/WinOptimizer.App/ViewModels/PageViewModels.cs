using System.IO;
using System.Collections.ObjectModel;
using System.Windows;
using WinOptimizer.Models;
using WinOptimizer.Services;

namespace WinOptimizer.ViewModels;

public abstract class PageViewModel(string title, string subtitle) : ViewModelBase
{
    public string Title { get; } = title;
    public string Subtitle { get; } = subtitle;
}

public sealed class OverviewViewModel : PageViewModel
{
    private int _historyLimit;
    private int _historyDurationMinutes;
    private SystemSnapshot _snapshot = SystemSnapshot.Empty;
    private FanReading? _selectedFan;

    public SystemSnapshot Snapshot { get => _snapshot; private set => Set(ref _snapshot, value); }
    public ObservableCollection<double> CpuHistory { get; } = [];
    public ObservableCollection<double> MemoryHistory { get; } = [];
    public ObservableCollection<double> GpuHistory { get; } = [];
    public ObservableCollection<double> DiskHistory { get; } = [];
    public ObservableCollection<FanReading> Fans { get; } = [];
    public double? CpuPercent => Snapshot.CpuUtilizationPercent;
    public string CpuPercentText => CpuPercent is null ? "—" : $"{CpuPercent:0}%";
    public string CpuStatus => MetricsMath.CpuStatus(CpuPercent);
    public string CpuFrequencyText => Snapshot.CpuFrequencyMhz is { } mhz ? $"{mhz / 1000:0.00} GHz · changes with workload" : "Frequency unavailable";
    public string CpuTemperatureText => Snapshot.CpuTemperatureCelsius is { } temp ? $"{temp:0}°C" : "Unavailable";
    public string CpuTemperatureSource => Snapshot.CpuTemperatureSource ?? "No CPU temperature sensor reported a value";
    public string MemoryStatus => MetricsMath.MemoryStatus(Snapshot.Memory);
    public string MemoryUsedText => Snapshot.Memory is { } m ? MetricsMath.FormatBytes(m.UsedBytes) : "—";
    public string MemoryAvailableText => Snapshot.Memory is { } m ? MetricsMath.FormatBytes(m.AvailableBytes) : "—";
    public string MemoryTotalText => Snapshot.Memory is { } m ? MetricsMath.FormatBytes(m.UsableBytes) : "—";
    public double MemoryPercent => Snapshot.Memory?.UsedPercent ?? 0;
    public string MemoryPercentText => Snapshot.Memory is { } memory ? $"{memory.UsedPercent:0}%" : "—";
    public bool HasMemory => Snapshot.Memory is not null;
    public string MemoryCommitText => Snapshot.Memory is { } m
        ? $"Committed {MetricsMath.FormatBytes(m.CommitUsedBytes)} of {MetricsMath.FormatBytes(m.CommitLimitBytes)}"
        : "Commit data unavailable";
    public string InstalledMemoryText => Snapshot.Memory?.InstalledBytes is { } installed
        ? $"{MetricsMath.FormatBytes(installed)} installed · {MetricsMath.FormatBytes(Snapshot.Memory.UsableBytes)} usable by Windows"
        : "Usable physical memory reported by Windows";
    public GpuReading? PrimaryGpu => Snapshot.Gpus.FirstOrDefault();
    public string GpuName => PrimaryGpu?.Name ?? "Graphics telemetry unavailable";
    public string GpuPercentText => PrimaryGpu?.UtilizationPercent is { } value ? $"{value:0}%" : "—";
    public double GpuPercent => PrimaryGpu?.UtilizationPercent ?? 0;
    public bool HasGpuLoad => PrimaryGpu?.UtilizationPercent is not null;
    public string GpuTemperatureText => PrimaryGpu?.TemperatureCelsius is { } temperature ? $"{temperature:0}°C" : "Unavailable";
    public string DiskActivityText => Snapshot.DiskActivityPercent is { } activity ? $"{activity:0}%" : "Unavailable";
    public double DiskActivity => Snapshot.DiskActivityPercent ?? 0;
    public string PowerPlanText => Snapshot.ActivePowerPlan ?? "Not reported";
    public string CpuName => Snapshot.CpuName ?? "Processor details unavailable";
    public string UpdateText => $"Updated {Snapshot.SampledAt:HH:mm:ss}";
    public FanReading? SelectedFan { get => _selectedFan; set { if (Set(ref _selectedFan, value)) { Raise(nameof(FanRpmText)); Raise(nameof(FanStatus)); } } }
    public string FanRpmText => SelectedFan is { State: FanState.Available, Rpm: { } rpm } ? $"{rpm:0} RPM" :
        SelectedFan is { State: FanState.ZeroRpm } ? "0 RPM · stopped" : "Fan speed unavailable";
    public string FanStatus => SelectedFan is { State: FanState.ZeroRpm } ? "Measured zero RPM" :
        SelectedFan is { State: FanState.Available } ? $"Measured · {SelectedFan.HardwareName}" :
        "Firmware may not expose a usable RPM sensor";
    public bool HasFanChoices => Fans.Count > 1;
    public bool HasDrives => Snapshot.Drives.Count > 0;
    public string HistoryWindowLabel => $"Up to {_historyDurationMinutes} minutes · fewer samples if the window is minimized";
    public string SystemStatusTitle
    {
        get
        {
            if (Snapshot.Memory is { } memory && memory.UsedPercent >= 90 && memory.AvailableBytes < Math.Max(512UL * 1024 * 1024, memory.UsableBytes / 10)) return "Available memory is low";
            if (Snapshot.Drives.FirstOrDefault(d => d.MountPoint.Equals(Path.GetPathRoot(Environment.SystemDirectory), StringComparison.OrdinalIgnoreCase)) is { FreePercent: < 10 }) return "System drive space is limited";
            if (Snapshot.CpuUtilizationPercent is >= 90) return "CPU load is high in this sample";
            if (Snapshot.CpuUtilizationPercent is null) return "Collecting the first measurements";
            return "No immediate pressure indicator in this sample";
        }
    }
    public string SystemStatusBody => Snapshot.CpuUtilizationPercent is null
        ? "CPU use is calculated from two Windows samples. Other readings become available independently."
        : "This summary uses visible measurements only: high CPU load in one sample, low available RAM, and system-drive space below 10%. It is not a benchmark or a health score.";

    public OverviewViewModel(int historyLimit, int historyDurationMinutes) : base("Overview", "A clear picture of what your PC is doing right now.")
    {
        _historyLimit = Math.Max(historyLimit, 2);
        _historyDurationMinutes = historyDurationMinutes;
    }

    public void Update(SystemSnapshot snapshot)
    {
        Snapshot = snapshot;
        Raise(nameof(CpuPercent)); Raise(nameof(CpuPercentText)); Raise(nameof(CpuStatus)); Raise(nameof(CpuFrequencyText));
        Raise(nameof(CpuTemperatureText)); Raise(nameof(CpuTemperatureSource)); Raise(nameof(MemoryStatus)); Raise(nameof(MemoryUsedText));
        Raise(nameof(MemoryAvailableText)); Raise(nameof(MemoryTotalText)); Raise(nameof(MemoryPercent)); Raise(nameof(MemoryPercentText)); Raise(nameof(HasMemory)); Raise(nameof(MemoryCommitText));
        Raise(nameof(InstalledMemoryText)); Raise(nameof(PrimaryGpu)); Raise(nameof(GpuName)); Raise(nameof(GpuPercentText)); Raise(nameof(GpuPercent)); Raise(nameof(HasGpuLoad));
        Raise(nameof(GpuTemperatureText)); Raise(nameof(DiskActivityText)); Raise(nameof(DiskActivity)); Raise(nameof(PowerPlanText));
        Raise(nameof(CpuName)); Raise(nameof(UpdateText));
        Raise(nameof(SystemStatusTitle)); Raise(nameof(SystemStatusBody)); Raise(nameof(HasDrives));
        ReplaceFans(snapshot.Fans);
        if (snapshot.CpuUtilizationPercent is { } cpu) AddBounded(CpuHistory, cpu);
        if (snapshot.Memory is { } memory) AddBounded(MemoryHistory, memory.UsedPercent);
        if (snapshot.Gpus.FirstOrDefault()?.UtilizationPercent is { } gpu) AddBounded(GpuHistory, gpu);
        if (snapshot.DiskActivityPercent is { } disk) AddBounded(DiskHistory, disk);
    }

    public void SetHistoryLimit(int capacity, int durationMinutes)
    {
        _historyLimit = Math.Clamp(capacity, 30, 3600);
        _historyDurationMinutes = durationMinutes;
        Trim(CpuHistory); Trim(MemoryHistory); Trim(GpuHistory); Trim(DiskHistory);
        Raise(nameof(HistoryWindowLabel));
    }

    private void ReplaceFans(IReadOnlyList<FanReading> readings)
    {
        var prior = SelectedFan?.Id;
        Fans.Clear();
        foreach (var fan in readings) Fans.Add(fan);
        SelectedFan = Fans.FirstOrDefault(f => f.Id == prior) ?? Fans.FirstOrDefault();
        Raise(nameof(HasFanChoices));
    }

    private void AddBounded(ObservableCollection<double> values, double value)
    {
        values.Add(value);
        while (values.Count > _historyLimit) values.RemoveAt(0);
    }

    private void Trim(ObservableCollection<double> values)
    {
        while (values.Count > _historyLimit) values.RemoveAt(0);
    }
}

public sealed class HardwareViewModel : PageViewModel
{
    private SystemSnapshot _snapshot = SystemSnapshot.Empty;
    private GpuReading? _selectedGpu;
    public SystemSnapshot Snapshot { get => _snapshot; private set => Set(ref _snapshot, value); }
    public ObservableCollection<GpuReading> Gpus { get; } = [];
    public ObservableCollection<FanReading> Fans { get; } = [];
    public GpuReading? SelectedGpu { get => _selectedGpu; set { if (Set(ref _selectedGpu, value)) { Raise(nameof(SelectedGpuDetail)); Raise(nameof(GpuUtilizationText)); Raise(nameof(GpuTemperatureText)); Raise(nameof(GpuClockText)); Raise(nameof(GpuMemoryText)); } } }
    public string SelectedGpuDetail => SelectedGpu is null
        ? "No graphics device exposed supported telemetry through the selected sensor interface."
        : SelectedGpu.Name;
    public string GpuUtilizationText => FormatPercent(SelectedGpu?.UtilizationPercent);
    public string GpuTemperatureText => FormatTemperature(SelectedGpu?.TemperatureCelsius);
    public string GpuClockText => FormatClock(SelectedGpu?.ClockMhz);
    public string GpuMemoryText => SelectedGpu?.MemoryUsedBytes is { } used && SelectedGpu.MemoryTotalBytes is { } total
        ? $"{MetricsMath.FormatBytes(used)} used of {MetricsMath.FormatBytes(total)}"
        : "VRAM sensor unavailable";
    public bool HasGpus => Gpus.Count > 0;
    public bool HasFans => Fans.Count > 0;
    public string CpuModel => Snapshot.CpuName ?? "Unavailable";
    public string CpuClock => Snapshot.CpuFrequencyMhz is { } speed ? $"{speed:0} MHz" : "Unavailable";
    public string CpuTemperature => Snapshot.CpuTemperatureCelsius is { } temp ? $"{temp:0.0}°C" : "Unavailable";
    public string CpuTemperatureSource => Snapshot.CpuTemperatureSource ?? "No supported sensor returned a reading";
    public string ProcessorCount => $"{Snapshot.LogicalProcessorCount} logical processors";
    public string SensorStateText => Snapshot.SensorState switch
    {
        ReadingState.Available => "Sensor source connected",
        ReadingState.Error => "Sensor read error",
        ReadingState.NotInitialized => "Sensor scan has not started",
        _ => "No supported sensor reading"
    };
    public string SensorMessage => Snapshot.SensorMessage;
    public HardwareViewModel() : base("Hardware", "Read the components Windows and your device sensors expose.") { }
    public void Update(SystemSnapshot snapshot)
    {
        Snapshot = snapshot;
        Replace(Gpus, snapshot.Gpus, SelectedGpu?.Id, out var selected);
        SelectedGpu = selected;
        Replace(Fans, snapshot.Fans, null, out _);
        Raise(nameof(SelectedGpuDetail)); Raise(nameof(GpuUtilizationText)); Raise(nameof(GpuTemperatureText)); Raise(nameof(GpuClockText));
        Raise(nameof(GpuMemoryText)); Raise(nameof(HasGpus)); Raise(nameof(HasFans)); Raise(nameof(CpuModel)); Raise(nameof(CpuClock)); Raise(nameof(CpuTemperature));
        Raise(nameof(CpuTemperatureSource)); Raise(nameof(ProcessorCount)); Raise(nameof(SensorStateText)); Raise(nameof(SensorMessage));
    }
    private static void Replace<T>(ObservableCollection<T> target, IReadOnlyList<T> source, string? selectedId, out T? selected) where T : class
    {
        target.Clear();
        foreach (var item in source) target.Add(item);
        selected = target.FirstOrDefault(item => item switch
        {
            GpuReading gpu => gpu.Id == selectedId,
            _ => false
        }) ?? target.FirstOrDefault();
    }
    private static string FormatPercent(double? value) => value is { } number ? $"{number:0}%" : "unavailable";
    private static string FormatTemperature(double? value) => value is { } number ? $"{number:0.0}°C" : "unavailable";
    private static string FormatClock(double? value) => value is { } number ? $"{number:0} MHz" : "unavailable";
}

public sealed class PerformanceViewModel : PageViewModel
{
    private bool _isRefreshing;
    private string _processStatus = "Open this page to collect a fresh process sample.";
    private SystemSnapshot _snapshot = SystemSnapshot.Empty;
    public ObservableCollection<ProcessUsage> Processes { get; } = [];
    public ObservableCollection<double> CpuHistory { get; }
    public ObservableCollection<double> MemoryHistory { get; }
    public SystemSnapshot Snapshot { get => _snapshot; private set { if (Set(ref _snapshot, value)) { Raise(nameof(CpuText)); Raise(nameof(MemoryText)); Raise(nameof(DiskText)); } } }
    public string CpuText => Snapshot.CpuUtilizationPercent is { } cpu ? $"{cpu:0}%" : "Collecting";
    public string MemoryText => Snapshot.Memory is { } memory ? $"{memory.UsedPercent:0}% in use" : "Unavailable";
    public string DiskText => Snapshot.DiskActivityPercent is { } disk ? $"{disk:0}% active" : "Unavailable";
    public bool IsRefreshing { get => _isRefreshing; private set => Set(ref _isRefreshing, value); }
    public string ProcessStatus { get => _processStatus; private set => Set(ref _processStatus, value); }
    public AsyncRelayCommand RefreshCommand { get; }
    private readonly Func<Task<IReadOnlyList<ProcessUsage>>> _read;
    public PerformanceViewModel(Func<Task<IReadOnlyList<ProcessUsage>>> read, ObservableCollection<double> cpuHistory, ObservableCollection<double> memoryHistory) : base("Performance", "See actual application resource use alongside system trends.")
    {
        _read = read; CpuHistory = cpuHistory; MemoryHistory = memoryHistory;
        RefreshCommand = new AsyncRelayCommand(async _ => await RefreshAsync());
    }
    public async Task RefreshAsync()
    {
        IsRefreshing = true;
        try
        {
            var rows = await _read();
            UpdateProcesses(rows);
        }
        catch (Exception ex)
        {
            ProcessStatus = "Windows could not read every process. Protected or exited processes may be omitted.";
            AppLog.Write("The process list could not be refreshed.", ex);
        }
        finally { IsRefreshing = false; }
    }

    public void UpdateProcesses(IReadOnlyList<ProcessUsage> rows)
    {
        Processes.Clear();
        foreach (var process in rows) Processes.Add(process);
        ProcessStatus = $"{rows.Count} processes sampled · CPU is a recent interval average; some protected processes hide details.";
    }
    public void UpdateSnapshot(SystemSnapshot snapshot) => Snapshot = snapshot;
}

public sealed class StartupViewModel : PageViewModel
{
    private readonly StartupManager _manager;
    private readonly ActivityLog _history;
    private readonly Func<Task> _changed;
    private string _status = "Startup items are not scanned until you open this section.";
    private bool _isScanning;
    public ObservableCollection<StartupItem> Items { get; } = [];
    public string Status { get => _status; private set => Set(ref _status, value); }
    public bool IsScanning { get => _isScanning; private set => Set(ref _isScanning, value); }
    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand ToggleCommand { get; }
    public StartupViewModel(StartupManager manager, ActivityLog history, Func<Task> changed)
        : base("Startup", "Review what launches at sign-in. Nothing is disabled automatically.")
    {
        _manager = manager; _history = history; _changed = changed;
        RefreshCommand = new AsyncRelayCommand(async _ => await RefreshAsync());
        ToggleCommand = new AsyncRelayCommand(async item => await ToggleAsync(item as StartupItem));
    }
    public async Task RefreshAsync()
    {
        IsScanning = true;
        try
        {
            var results = await _manager.ScanAsync();
            Items.Clear();
            foreach (var item in results) Items.Add(item);
            Status = $"{results.Count(item => item.IsEnabled)} enabled items · {results.Count(item => !item.IsEnabled)} backed-up disabled items · impact data is unavailable unless Windows provides it.";
        }
        catch (Exception ex) { Status = "Startup entries could not be read."; AppLog.Write("Startup scan failed.", ex); }
        finally { IsScanning = false; }
    }
    private async Task ToggleAsync(StartupItem? item)
    {
        if (item is null || !item.CanChange) return;
        var action = item.IsEnabled ? "Disable" : "Restore";
        var detail = item.IsEnabled
            ? $"Remove this current-user registry value from Windows startup and keep a local backup?\n\n{item.Name}\n{item.Command}\n\nThis will not stop a program that is already running."
            : $"Restore this backed-up current-user startup value?\n\n{item.Name}\n{item.Command}";
        if (MessageBox.Show(detail, $"{action} startup item?", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        var outcome = _manager.SetEnabled(item, !item.IsEnabled);
        Status = outcome.Message;
        await _history.AddAsync(new ActivityEntry(Guid.NewGuid(), DateTimeOffset.Now,
            $"{action} startup item: {item.Name}", outcome.Message,
            outcome.Succeeded ? "Verified" : "Not changed", outcome.Succeeded ? item.Id : null));
        await RefreshAsync();
        await _changed();
    }
}

public sealed class StorageViewModel : PageViewModel
{
    private readonly SafeCleanupService _cleanup;
    private readonly ActivityLog _history;
    private CleanupPreview? _preview;
    private bool _isScanning;
    private bool _isApplying;
    private string _status = "Only fixed drives that Windows reports as ready are shown.";
    public ObservableCollection<StorageDriveReading> Drives { get; } = [];
    public CleanupPreview? Preview { get => _preview; private set { if (Set(ref _preview, value)) { Raise(nameof(PreviewSummary)); Raise(nameof(PreviewReady)); Raise(nameof(HasPreview)); CleanCommand?.NotifyCanExecuteChanged(); } } }
    public bool IsScanning { get => _isScanning; private set => Set(ref _isScanning, value); }
    public bool IsApplying { get => _isApplying; private set { if (Set(ref _isApplying, value)) CleanCommand.NotifyCanExecuteChanged(); } }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public string PreviewSummary => Preview is null ? "Review older temporary files" :
        $"{Preview.Candidates.Count} eligible files · {MetricsMath.FormatBytes(Preview.TotalBytes)} · changed before {Preview.Cutoff.ToLocalTime():MMM d, yyyy}";
    public bool PreviewReady => Preview is { TotalBytes: > 0 };
    public bool HasPreview => Preview is not null;
    public AsyncRelayCommand PreviewCommand { get; }
    public AsyncRelayCommand CleanCommand { get; }
    private readonly Action _refreshDrives;
    public StorageViewModel(SafeCleanupService cleanup, ActivityLog history, Action refreshDrives)
        : base("Storage", "Understand free space before deciding what to remove.")
    {
        _cleanup = cleanup; _history = history; _refreshDrives = refreshDrives;
        PreviewCommand = new AsyncRelayCommand(async _ => await PreviewCleanupAsync());
        CleanCommand = new AsyncRelayCommand(async _ => await ApplyCleanupAsync(), _ => PreviewReady && !IsApplying);
    }
    public void Update(SystemSnapshot snapshot)
    {
        Drives.Clear();
        foreach (var drive in snapshot.Drives) Drives.Add(drive);
    }
    private async Task PreviewCleanupAsync()
    {
        IsScanning = true;
        try
        {
            Preview = await _cleanup.PreviewAsync();
            Status = Preview.TotalBytes > 0
                ? Preview.Scope
                : "No eligible old temporary files were found. Personal folders, subfolders, and other file types are not included.";
        }
        catch (Exception ex) { Status = "The temporary-file preview could not be completed."; AppLog.Write("Temporary-file preview failed.", ex); }
        finally { IsScanning = false; }
    }
    private async Task ApplyCleanupAsync()
    {
        if (Preview is not { TotalBytes: > 0 } preview) return;
        var question = $"Remove {preview.Candidates.Count} files totaling up to {MetricsMath.FormatBytes(preview.TotalBytes)}?\n\nScope: {preview.Scope}\n\nLocked or changed files will be skipped. This action cannot restore files after Windows deletes them.";
        if (MessageBox.Show(question, "Review temporary-file cleanup", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        IsApplying = true;
        try
        {
            var result = await _cleanup.ApplyAsync(preview);
            var summary = $"Windows confirmed {result.DeletedCount} files were removed; their recorded file size was {MetricsMath.FormatBytes(result.RecoveredBytes)}. This is an estimate of freed space. {result.SkippedCount} files were skipped.";
            Status = summary;
            Preview = null;
            await _history.AddAsync(new ActivityEntry(Guid.NewGuid(), result.CompletedAt,
                "Temporary-file cleanup", summary, result.SkippedCount == 0 ? "Verified" : "Completed with skipped files"));
            _refreshDrives();
        }
        catch (Exception ex) { Status = "Cleanup stopped before completion. Check the local log for details."; AppLog.Write("Temporary-file cleanup failed.", ex); }
        finally { IsApplying = false; }
    }
}

public sealed class OptimizeViewModel : PageViewModel
{
    private string _status = "Recommendations are based on Windows measurements and a read-only scan.";
    private string _driveStatus = "Windows will choose the appropriate maintenance for this drive's media type.";
    private StorageDriveReading? _selectedDrive;
    private bool _isDriveMaintenanceRunning;
    public ObservableCollection<OptimizationRecommendation> Recommendations { get; } = [];
    public ObservableCollection<StorageDriveReading> Drives { get; } = [];
    public string Status { get => _status; set => Set(ref _status, value); }
    public string DriveStatus { get => _driveStatus; private set => Set(ref _driveStatus, value); }
    public StorageDriveReading? SelectedDrive { get => _selectedDrive; set { if (Set(ref _selectedDrive, value)) OptimizeDriveCommand.NotifyCanExecuteChanged(); } }
    public bool IsDriveMaintenanceRunning { get => _isDriveMaintenanceRunning; private set { if (Set(ref _isDriveMaintenanceRunning, value)) OptimizeDriveCommand.NotifyCanExecuteChanged(); } }
    public RelayCommand ActionCommand { get; }
    public RelayCommand ReviewAllCommand { get; }
    public RelayCommand WindowsFeaturesCommand { get; }
    public RelayCommand CompanionToolsCommand { get; }
    public AsyncRelayCommand OptimizeDriveCommand { get; }
    private readonly Action<string> _navigate;
    private readonly DriveMaintenanceService _driveMaintenance;
    private readonly ActivityLog _history;
    public OptimizeViewModel(Action<string> navigate, DriveMaintenanceService driveMaintenance, ActivityLog history) : base("Optimize", "Careful, hardware-aware ways to make this PC feel more responsive.")
    {
        _navigate = navigate; _driveMaintenance = driveMaintenance; _history = history;
        ActionCommand = new RelayCommand(id => { if (id is string value) _navigate(value); });
        ReviewAllCommand = new RelayCommand(_ => ReviewAll());
        WindowsFeaturesCommand = new RelayCommand(_ => OpenWindowsFeatures());
        CompanionToolsCommand = new RelayCommand(url => OpenCompanionTool(url as string));
        OptimizeDriveCommand = new AsyncRelayCommand(async _ => await OptimizeDriveAsync(), _ => SelectedDrive is not null && !IsDriveMaintenanceRunning);
    }

    private void ReviewAll()
    {
        var findings = Recommendations.Where(item => item.State == RecommendationState.Applicable).ToArray();
        var summary = findings.Length == 0
            ? "The latest scan found no currently applicable recommendations. You can still review startup apps, temporary-file cleanup, and drive maintenance below."
            : string.Join("\n", findings.Select(item => $"• {item.Title} — {item.ExpectedBenefit}"));
        MessageBox.Show($"Review before making changes\n\n{summary}\n\nNothing has been changed. Each item needs its own review because the trade-offs differ; temporary-file removal is permanent, and feature changes can require a restart.", "Optimize all · review", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OpenWindowsFeatures()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("OptionalFeatures.exe") { UseShellExecute = true });
            Status = "Windows Features opened. Review each item in Windows; changing components may require a restart.";
        }
        catch (Exception ex)
        {
            Status = "Windows Features could not be opened. Search Windows for “Turn Windows features on or off”.";
            AppLog.Write("Could not open Windows Optional Features.", ex);
        }
    }

    private static void OpenCompanionTool(string? url)
    {
        var allowed = url switch
        {
            "autoruns" => "https://learn.microsoft.com/sysinternals/downloads/autoruns",
            "process-explorer" => "https://learn.microsoft.com/sysinternals/downloads/process-explorer",
            "wiztree" => "https://www.diskanalyzer.com/download",
            _ => null
        };
        if (allowed is null) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(allowed) { UseShellExecute = true }); }
        catch (Exception ex) { AppLog.Write("Could not open a companion-tool download page.", ex); }
    }
    public void Update(IEnumerable<OptimizationRecommendation> recommendations, IEnumerable<StorageDriveReading> drives)
    {
        Recommendations.Clear();
        foreach (var recommendation in recommendations) Recommendations.Add(recommendation);
        var priorDrive = SelectedDrive?.MountPoint;
        Drives.Clear();
        foreach (var drive in drives) Drives.Add(drive);
        SelectedDrive = Drives.FirstOrDefault(drive => drive.MountPoint.Equals(priorDrive, StringComparison.OrdinalIgnoreCase)) ?? Drives.FirstOrDefault();
        Status = $"{Recommendations.Count(item => item.State == RecommendationState.Applicable)} applicable finding(s) · no changes are applied automatically.";
    }

    private async Task OptimizeDriveAsync()
    {
        if (SelectedDrive is not { } drive || DriveMaintenanceService.NormalizeVolume(drive.MountPoint) is not { } volume) return;
        var result = MessageBox.Show(
            $"Windows will run its media-aware maintenance on {volume}. The built-in /O operation chooses the Windows-supported method for the drive type. This can use disk activity and may take time; no files are removed. Windows may ask for administrator approval. Continue?",
            "Review drive maintenance", MessageBoxButton.YesNo, MessageBoxImage.Information);
        if (result != MessageBoxResult.Yes) return;

        IsDriveMaintenanceRunning = true;
        DriveStatus = $"Windows is running media-aware maintenance for {volume}. Follow the Windows progress window.";
        try
        {
            var outcome = await _driveMaintenance.RunAsync(volume);
            DriveStatus = outcome.Message;
            await _history.AddAsync(new ActivityEntry(Guid.NewGuid(), DateTimeOffset.Now,
                $"Drive maintenance · {volume}", outcome.Message,
                outcome.Succeeded ? "Completed by Windows · no undo" : outcome.UserCancelled ? "Cancelled" : "Not completed"));
        }
        catch (Exception ex)
        {
            DriveStatus = "Drive maintenance did not complete. Check the local log for details.";
            AppLog.Write("Drive maintenance operation failed.", ex);
        }
        finally { IsDriveMaintenanceRunning = false; }
    }
}

public sealed class HistoryViewModel : PageViewModel
{
    private readonly ActivityLog _history;
    private readonly StartupManager _startup;
    private readonly PowerPlanService _powerPlans;
    public ObservableCollection<ActivityEntry> Items { get; } = [];
    private string _status = "Changes are recorded only on this computer.";
    public string Status { get => _status; private set => Set(ref _status, value); }
    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand UndoCommand { get; }
    public HistoryViewModel(ActivityLog history, StartupManager startup, PowerPlanService powerPlans) : base("History", "A local record of reviewed and verified changes.")
    {
        _history = history; _startup = startup; _powerPlans = powerPlans;
        RefreshCommand = new AsyncRelayCommand(async _ => await RefreshAsync());
        UndoCommand = new AsyncRelayCommand(async entry => await UndoAsync(entry as ActivityEntry));
    }
    public async Task RefreshAsync()
    {
        var entries = await _history.ReadAsync();
        Items.Clear();
        foreach (var item in entries) Items.Add(item);
        Status = entries.Count == 0 ? "No optimization changes have been recorded yet." : $"{entries.Count} recent event(s) · stored locally in your profile.";
    }
    private async Task UndoAsync(ActivityEntry? entry)
    {
        if (entry?.UndoData is not { } id) return;
        if (id.StartsWith("power-plan:", StringComparison.Ordinal) && TryReadPowerPlanUndo(id, out var previousPlan, out var expectedPlan))
        {
            if (MessageBox.Show("Restore the power plan that was active before this change? The current plan is checked first so a later Windows change is preserved.",
                    "Restore previous power plan", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            var powerResult = await Task.Run(() => _powerPlans.Restore(previousPlan, expectedPlan));
            Status = powerResult.Message;
            await _history.AddAsync(new ActivityEntry(Guid.NewGuid(), DateTimeOffset.Now,
                powerResult.Succeeded ? "Restored previous power plan" : "Power-plan restoration", powerResult.Message,
                powerResult.Succeeded ? "Verified by Windows" : powerResult.IsConflict ? "Conflict · not changed" : "Not restored",
                powerResult.Succeeded ? $"power-plan:{expectedPlan:D}:{previousPlan:D}" : null));
            await RefreshAsync();
            return;
        }
        var current = (await _startup.ScanAsync()).FirstOrDefault(item => item.Id == id && item.CanChange);
        if (current is null) { Status = "The original startup entry or its local backup could not be found."; return; }
        var desiredState = !current.IsEnabled;
        var operation = desiredState ? "restore" : "disable";
        if (MessageBox.Show($"{operation} this current-user startup entry?\n\n{current.Name}\n{current.Command}",
                "Review startup change", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        var result = _startup.SetEnabled(current, desiredState);
        Status = result.Message;
        await _history.AddAsync(new ActivityEntry(Guid.NewGuid(), DateTimeOffset.Now,
            $"Restored startup configuration: {current.Name}", result.Message,
            result.Succeeded ? "Verified" : "Not changed", result.Succeeded ? id : null));
        await RefreshAsync();
    }

    private static bool TryReadPowerPlanUndo(string data, out Guid previous, out Guid expected)
    {
        previous = Guid.Empty; expected = Guid.Empty;
        var parts = data.Split(':');
        return parts.Length == 3 && Guid.TryParse(parts[1], out previous) && Guid.TryParse(parts[2], out expected);
    }
}

public sealed class SettingsViewModel : PageViewModel
{
    private readonly UserPreferences _preferences;
    private readonly SettingsStore _store;
    private readonly Action _openSetup;
    private ThemeChoice _theme;
    private int _pollInterval;
    private int _historyDuration;
    private RecommendationLevel _level;
    private string _saveStatus = "Settings are stored locally and can be changed at any time.";
    public IReadOnlyList<ThemeChoice> ThemeOptions { get; } = Enum.GetValues<ThemeChoice>();
    public IReadOnlyList<RecommendationLevel> LevelOptions { get; } = Enum.GetValues<RecommendationLevel>();
    public IReadOnlyList<int> PollOptions { get; } = [2, 5, 10];
    public IReadOnlyList<int> HistoryOptions { get; } = [1, 5, 15, 30];
    public ThemeChoice Theme { get => _theme; set { if (Set(ref _theme, value)) { _preferences.Theme = value; ThemeService.Apply(value); Save(); } } }
    public int PollInterval { get => _pollInterval; set { if (Set(ref _pollInterval, value)) { _preferences.PollIntervalSeconds = Math.Clamp(value, 2, 10); Save(); } } }
    public int HistoryDuration { get => _historyDuration; set { if (Set(ref _historyDuration, value)) { _preferences.HistoryDurationMinutes = value; Save(); } } }
    public RecommendationLevel Level { get => _level; set { if (Set(ref _level, value)) { _preferences.RecommendationLevel = value; Save(); } } }
    public string SaveStatus { get => _saveStatus; private set => Set(ref _saveStatus, value); }
    public RelayCommand RestartSetupCommand { get; }
    public SettingsViewModel(UserPreferences preferences, SettingsStore store, Action openSetup)
        : base("Settings", "Set your theme and choose how often measurements update.")
    {
        _preferences = preferences; _store = store; _openSetup = openSetup;
        _theme = preferences.Theme; _pollInterval = preferences.PollIntervalSeconds;
        _historyDuration = preferences.HistoryDurationMinutes; _level = preferences.RecommendationLevel;
        RestartSetupCommand = new RelayCommand(_ => _openSetup());
    }
    private async void Save()
    {
        try { await _store.SaveAsync(_preferences); SaveStatus = "Saved locally."; }
        catch (Exception ex) { SaveStatus = "Could not save settings."; AppLog.Write("Settings could not be saved.", ex); }
    }
}
