namespace WinOptimizer.Models;

public sealed record RecommendationContext(
    SystemSnapshot Snapshot,
    int EnabledCurrentUserStartupItems,
    int EligibleTemporaryFiles,
    ulong EligibleTemporaryBytes,
    RecommendationLevel Level = RecommendationLevel.Safe);

public sealed record ChangeRecord(
    string Id,
    string Description,
    DateTimeOffset Timestamp,
    string Result,
    string? BackupPath = null,
    string? Details = null);
