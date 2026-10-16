using System.IO;
using WinOptimizer.Models;

namespace WinOptimizer.Services;

public sealed class RecommendationEngine
{
    public IReadOnlyList<OptimizationRecommendation> Evaluate(RecommendationContext context)
    {
        var items = new List<OptimizationRecommendation>();
        var systemDrive = context.Snapshot.Drives.FirstOrDefault(d =>
            string.Equals(d.MountPoint, Path.GetPathRoot(Environment.SystemDirectory), StringComparison.OrdinalIgnoreCase));

        var lowSpaceThreshold = context.Level == RecommendationLevel.Safe ? 10 : context.Level == RecommendationLevel.Balanced ? 15 : 20;
        if (systemDrive is { TotalBytes: > 0 } && systemDrive.FreePercent < lowSpaceThreshold)
        {
            items.Add(new OptimizationRecommendation(
                "storage.low-space", "Your system drive has limited free space", "Storage",
                $"{systemDrive.MountPoint} has {MetricsMath.FormatBytes(systemDrive.FreeBytes)} free ({systemDrive.FreePercent:0.#}%). Windows and applications need working room for updates and temporary files.",
                context.EligibleTemporaryBytes > 0
                    ? $"Review up to {MetricsMath.FormatBytes(context.EligibleTemporaryBytes)} of older temporary files"
                    : "Review large files and apps before removing anything",
                context.EligibleTemporaryBytes > 0 ? "Measured file list" : "Context dependent",
                RecommendationRisk.Low,
                RecommendationState.Applicable,
                context.EligibleTemporaryBytes > 0 ? "Review cleanup" : "View storage",
                true));
        }

        var startupThreshold = context.Level == RecommendationLevel.Safe ? 8 : context.Level == RecommendationLevel.Balanced ? 5 : 3;
        if (context.EnabledCurrentUserStartupItems >= startupThreshold)
        {
            items.Add(new OptimizationRecommendation(
                "startup.review", "Review programs that start with Windows", "Startup",
                $"{context.EnabledCurrentUserStartupItems} current-user startup entries are enabled. Some may be useful; Windows does not expose reliable impact data for every entry.",
                "Fewer sign-in apps can reduce background activity after sign-in",
                "Configuration count only", RecommendationRisk.Informational,
                RecommendationState.Applicable, "Review startup", true));
        }

        if (context.Snapshot.Memory is { } memory && memory.UsedPercent >= 90 &&
            memory.AvailableBytes < Math.Max(512UL * 1024 * 1024, memory.UsableBytes / 10))
        {
            items.Add(new OptimizationRecommendation(
                "memory.pressure", "Available memory is low right now", "Memory",
                $"Windows reports {MetricsMath.FormatBytes(memory.AvailableBytes)} of usable physical RAM available. This is a point-in-time reading; cached memory can be reused, and high usage alone is not a fault.",
                "If the PC feels slow, close or pause work you recognize before changing system settings",
                "Windows memory status", RecommendationRisk.Informational,
                RecommendationState.Applicable, "See processes", true));
        }

        if (items.Count == 0)
        {
            items.Add(new OptimizationRecommendation(
                "system.no-action", "No changes need your attention", "System",
                "The measurements available right now do not point to a clear, safe optimization. WinOptimizer will keep observing and will not change settings automatically.",
                "No change recommended", "Current measurements", RecommendationRisk.Informational,
                RecommendationState.Unnecessary, "View measurements", false));
        }

        return items;
    }
}
