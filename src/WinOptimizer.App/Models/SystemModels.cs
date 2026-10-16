using WinOptimizer.Services;

namespace WinOptimizer.Models;

public enum ReadingState { NotInitialized, Available, Unavailable, Error }
public enum FanState { NotInitialized, Available, ZeroRpm, Unavailable, Error }
public enum ThemeChoice { Dark, Light, System }
public enum RecommendationLevel { Safe, Balanced, Advanced }
public enum RecommendationRisk { Informational, Low, Moderate }
public enum RecommendationState { Applicable, AlreadyApplied, Unnecessary, Unsupported, Failed }

public sealed record MemoryReading(
    ulong UsableBytes,
    ulong AvailableBytes,
    ulong CommitUsedBytes,
    ulong CommitLimitBytes,
    ulong? InstalledBytes)
{
    public ulong UsedBytes => UsableBytes >= AvailableBytes ? UsableBytes - AvailableBytes : 0;
    public double UsedPercent => MetricsMath.Percent((double)UsedBytes, UsableBytes);
    public double AvailablePercent => MetricsMath.Percent((double)AvailableBytes, UsableBytes);
    public double CommitPercent => MetricsMath.Percent((double)CommitUsedBytes, CommitLimitBytes);
}

public sealed record StorageDriveReading(
    string Name,
    string MountPoint,
    string FileSystem,
    string? Model,
    string? MediaType,
    ulong TotalBytes,
    ulong FreeBytes)
{
    public ulong UsedBytes => TotalBytes >= FreeBytes ? TotalBytes - FreeBytes : 0;
    public double UsedPercent => MetricsMath.Percent((double)UsedBytes, TotalBytes);
    public double FreePercent => MetricsMath.Percent((double)FreeBytes, TotalBytes);
    public string FreeText => $"{MetricsMath.FormatBytes(FreeBytes)} free";
    public string CapacityText => $"{MetricsMath.FormatBytes(UsedBytes)} used of {MetricsMath.FormatBytes(TotalBytes)}";
    public string DriveOptionText => $"{Name}  ·  {FileSystem}  ·  {MetricsMath.FormatBytes(FreeBytes)} free";
    public string SpaceStatus => FreePercent < 10 ? "Very little space available" : FreePercent < 20 ? "Free space is limited" : FreeText;
}

public sealed record GpuReading(
    string Id,
    string Name,
    double? UtilizationPercent,
    ulong? MemoryUsedBytes,
    ulong? MemoryTotalBytes,
    double? TemperatureCelsius,
    double? ClockMhz,
    string? TemperatureSource,
    string? UtilizationSource);

public sealed record FanReading(
    string Id,
    string Name,
    string HardwareName,
    double? Rpm,
    FanState State,
    string Source)
{
    public string RpmText => State switch
    {
        FanState.Available when Rpm is { } value => $"{value:0} RPM",
        FanState.ZeroRpm => "0 RPM · measured",
        FanState.NotInitialized => "Reading not initialized",
        FanState.Error => "Sensor read error",
        _ => "RPM unavailable"
    };
    public string StatusText => State switch
    {
        FanState.Available => "Measured speed",
        FanState.ZeroRpm => "Sensor reports zero RPM",
        FanState.NotInitialized => "Waiting for first sensor sample",
        FanState.Error => "Sensor could not be read",
        _ => "No usable fan reading"
    };
}

public sealed record SystemSnapshot(
    DateTimeOffset SampledAt,
    string ComputerName,
    string WindowsVersion,
    string Architecture,
    int LogicalProcessorCount,
    string? CpuName,
    double? CpuUtilizationPercent,
    double? CpuFrequencyMhz,
    double? CpuTemperatureCelsius,
    string? CpuTemperatureSource,
    MemoryReading? Memory,
    IReadOnlyList<GpuReading> Gpus,
    IReadOnlyList<FanReading> Fans,
    IReadOnlyList<StorageDriveReading> Drives,
    double? DiskActivityPercent,
    string? ActivePowerPlan,
    ReadingState SensorState,
    string SensorMessage)
{
    public static SystemSnapshot Empty { get; } = new(
        DateTimeOffset.Now,
        Environment.MachineName,
        Environment.OSVersion.VersionString,
        Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit",
        Environment.ProcessorCount,
        null,
        null,
        null,
        null,
        null,
        null,
        [],
        [],
        [],
        null,
        null,
        ReadingState.NotInitialized,
        "Waiting for the first Windows measurement.");
}

public sealed record ProcessUsage(int Id, string Name, double? CpuPercent, ulong WorkingSetBytes, string? Path)
{
    public string CpuText => CpuPercent is { } cpu ? $"{cpu:0.0}%" : "Collecting";
    public string MemoryText => MetricsMath.FormatBytes(WorkingSetBytes);
    public string PathText => Path ?? "Path unavailable";
}

public sealed record StartupItem(
    string Id,
    string Name,
    string Command,
    string Location,
    string Scope,
    string? Publisher,
    bool IsEnabled,
    bool CanChange,
    string? Advisory)
{
    public string StateText => IsEnabled ? "Enabled" : "Disabled · backup saved";
    public string ActionLabel => !CanChange ? "Read only" : IsEnabled ? "Disable" : "Restore";
}

public sealed record OptimizationRecommendation(
    string Id,
    string Title,
    string Category,
    string Explanation,
    string ExpectedBenefit,
    string Confidence,
    RecommendationRisk Risk,
    RecommendationState State,
    string ActionLabel,
    bool HasAction);

public sealed record CleanupCandidate(string Path, ulong SizeBytes, DateTime LastWriteTime)
{
    public string SizeText => MetricsMath.FormatBytes(SizeBytes);
}
public sealed record CleanupPreview(IReadOnlyList<CleanupCandidate> Candidates, ulong TotalBytes, string Scope, DateTime Cutoff);
public sealed record CleanupResult(int DeletedCount, ulong RecoveredBytes, int SkippedCount, DateTimeOffset CompletedAt);

public sealed record ActivityEntry(
    Guid Id,
    DateTimeOffset Timestamp,
    string Title,
    string Detail,
    string Outcome,
    string? UndoData = null)
{
    public bool CanUndo => !string.IsNullOrWhiteSpace(UndoData);
    public string UndoActionLabel => UndoData?.StartsWith("power-plan:", StringComparison.Ordinal) == true ? "Restore previous plan" : "Reverse startup change";
}

public sealed class UserPreferences
{
    public bool SetupCompleted { get; set; }
    public ThemeChoice Theme { get; set; } = ThemeChoice.Dark;
    public RecommendationLevel RecommendationLevel { get; set; } = RecommendationLevel.Safe;
    public int PollIntervalSeconds { get; set; } = 2;
    public int HistoryDurationMinutes { get; set; } = 5;
}

