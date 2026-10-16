namespace WinOptimizer.Models;

public sealed record RecommendationContext(
    SystemSnapshot Snapshot,
    int EnabledCurrentUserStartupItems,
    int EligibleTemporaryFiles,
    ulong EligibleTemporaryBytes,
    RecommendationLevel Level = RecommendationLevel.Safe,
    bool HighPerformancePlanAvailable = false,
    bool IsOnAcPower = false,
    Guid? ActivePowerPlanId = null);

public sealed record ChangeRecord(
    string Id,
    string Description,
    DateTimeOffset Timestamp,
    string Result,
    string? BackupPath = null,
    string? Details = null);

