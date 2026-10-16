using System.IO;
using WinOptimizer.Controls;
using WinOptimizer.Models;
using WinOptimizer.Services;
using Xunit;

namespace WinOptimizer.Tests;

public sealed class TelemetryAndVisualizationTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(50, 50)]
    [InlineData(100, 100)]
    [InlineData(135, 100)]
    [InlineData(-5, 0)]
    public void Cpu_gauge_maps_utilization_to_a_bounded_sweep(double input, double expectedPercent)
    {
        var expectedAngle = 135 + expectedPercent * 2.7;
        Assert.Equal(expectedAngle, CpuGauge.IndicatorAngle(input), precision: 8);
    }

    [Fact]
    public void Fan_animation_stops_when_unavailable_or_measured_at_zero()
    {
        Assert.False(FanVisual.ShouldAnimate(null, FanState.Unavailable, true));
        Assert.False(FanVisual.ShouldAnimate(0, FanState.ZeroRpm, true));
        Assert.False(FanVisual.ShouldAnimate(1600, FanState.Available, false));
        Assert.True(FanVisual.ShouldAnimate(1600, FanState.Available, true));
    }

    [Fact]
    public void Fan_motion_speed_increases_with_rpm_and_is_capped_for_smooth_rendering()
    {
        Assert.Equal(0, FanVisual.AngularSpeedDegreesPerSecond(0));
        Assert.Equal(180, FanVisual.AngularSpeedDegreesPerSecond(1000));
        Assert.True(FanVisual.AngularSpeedDegreesPerSecond(3000) > FanVisual.AngularSpeedDegreesPerSecond(1000));
        Assert.Equal(720, FanVisual.AngularSpeedDegreesPerSecond(10000));
    }

    [Fact]
    public void Memory_calculations_keep_physical_and_committed_memory_separate()
    {
        var memory = new MemoryReading(16_000, 4_000, 21_000, 32_000, 17_000);
        Assert.Equal(12_000UL, memory.UsedBytes);
        Assert.Equal(75d, memory.UsedPercent);
        Assert.Equal(65.625, memory.CommitPercent, precision: 6);
        Assert.Equal("Windows still has physical memory available", MetricsMath.MemoryStatus(memory));
    }

    [Theory]
    [InlineData(1023UL, "1023 B")]
    [InlineData(1024UL, "1.0 KB")]
    [InlineData(1_048_576UL, "1.0 MB")]
    [InlineData(1_073_741_824UL, "1.0 GB")]
    [InlineData(1_099_511_627_776UL, "1.0 TB")]
    public void Byte_format_uses_the_correct_binary_unit_scale(ulong bytes, string expected) =>
        Assert.Equal(expected, MetricsMath.FormatBytes(bytes));

    [Fact]
    public void History_chart_maps_first_and_last_samples_to_plot_edges()
    {
        Assert.Equal(2, HistoryChart.SampleX(0, 4, 102, 2));
        Assert.Equal(100, HistoryChart.SampleX(3, 4, 102, 2));
        Assert.Equal(51, HistoryChart.SampleX(0, 1, 102, 2));
    }

    [Fact]
    public void History_buffer_evicts_oldest_sample_without_growing_past_its_limit()
    {
        var history = new HistoryBuffer<int>(3);
        history.Add(1); history.Add(2); history.Add(3); history.Add(4);
        Assert.Equal(new[] { 2, 3, 4 }, history.Items);
        Assert.Equal(3, history.Count);
    }

    [Fact]
    public void Invalid_or_missing_sensor_values_are_not_converted_to_zero()
    {
        Assert.Null(MetricsMath.ValidatePercent(null));
        Assert.Null(MetricsMath.ValidatePercent(double.NaN));
        Assert.Null(MetricsMath.ValidatePercent(101));
        Assert.Equal(60d, MetricsMath.ValidatePercent(60));
        Assert.Equal(ReadingState.NotInitialized, SystemSnapshot.Empty.SensorState);
        Assert.Null(SystemSnapshot.Empty.CpuUtilizationPercent);
        Assert.Empty(SystemSnapshot.Empty.Fans);
    }

    [Fact]
    public void Recommendation_engine_does_not_invent_a_finding_for_an_empty_snapshot()
    {
        var engine = new RecommendationEngine();
        var results = engine.Evaluate(new RecommendationContext(SystemSnapshot.Empty, 0, 0, 0));
        Assert.Single(results);
        Assert.Equal(RecommendationState.Unnecessary, results[0].State);
        Assert.False(results[0].HasAction);
    }

    [Fact]
    public void Startup_review_threshold_tracks_the_selected_recommendation_profile()
    {
        var engine = new RecommendationEngine();
        var safe = engine.Evaluate(new RecommendationContext(SystemSnapshot.Empty, 5, 0, 0, RecommendationLevel.Safe));
        var balanced = engine.Evaluate(new RecommendationContext(SystemSnapshot.Empty, 5, 0, 0, RecommendationLevel.Balanced));
        Assert.DoesNotContain(safe, item => item.Id == "startup.review");
        Assert.Contains(balanced, item => item.Id == "startup.review" && item.State == RecommendationState.Applicable);
    }

    [Fact]
    public void Safe_temporary_file_cleanup_is_a_user_review_and_never_a_background_action()
    {
        var recommendation = new OptimizationRecommendation(
            "storage.low-space", "Review old files", "Storage", "Evidence", "Review list", "Measured file list",
            RecommendationRisk.Low, RecommendationState.Applicable, "Review cleanup", true);
        Assert.True(recommendation.HasAction);
        Assert.Equal(RecommendationRisk.Low, recommendation.Risk);
        Assert.Equal(RecommendationState.Applicable, recommendation.State);
    }

    [Fact]
    public async Task Cleanup_preview_scans_only_old_top_level_temp_files_and_apply_rechecks_them()
    {
        var isolatedTemp = Path.Combine(Path.GetTempPath(), "WinOptimizer.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(isolatedTemp);
        try
        {
            var oldFile = Path.Combine(isolatedTemp, "old.tmp");
            var recentFile = Path.Combine(isolatedTemp, "recent.tmp");
            var personalFile = Path.Combine(isolatedTemp, "notes.txt");
            var nestedDirectory = Path.Combine(isolatedTemp, "cache");
            Directory.CreateDirectory(nestedDirectory);
            File.WriteAllBytes(oldFile, [1, 2, 3, 4]);
            File.WriteAllText(recentFile, "recent");
            File.WriteAllText(personalFile, "personal");
            File.WriteAllText(Path.Combine(nestedDirectory, "old.cache"), "nested");
            File.SetLastWriteTimeUtc(oldFile, DateTime.UtcNow.AddDays(-45));
            File.SetLastWriteTimeUtc(recentFile, DateTime.UtcNow.AddDays(-2));
            File.SetLastWriteTimeUtc(personalFile, DateTime.UtcNow.AddDays(-45));
            File.SetLastWriteTimeUtc(Path.Combine(nestedDirectory, "old.cache"), DateTime.UtcNow.AddDays(-45));

            var service = new SafeCleanupService(isolatedTemp);
            var preview = service.Preview();
            Assert.Single(preview.Candidates);
            Assert.Equal(4UL, preview.TotalBytes);
            Assert.True(File.Exists(oldFile), "preview does not delete anything");

            File.SetLastWriteTimeUtc(oldFile, DateTime.UtcNow.AddDays(-1));
            var changed = await service.ApplyAsync(preview);
            Assert.Equal(0, changed.DeletedCount);
            Assert.Equal(1, changed.SkippedCount);
            Assert.True(File.Exists(oldFile), "a file changed after preview is preserved");

            File.SetLastWriteTimeUtc(oldFile, DateTime.UtcNow.AddDays(-45));
            var freshPreview = service.Preview();
            var applied = await service.ApplyAsync(freshPreview);
            Assert.Equal(1, applied.DeletedCount);
            Assert.Equal(0, applied.SkippedCount);
            Assert.False(File.Exists(oldFile));
            Assert.True(File.Exists(personalFile));
            Assert.True(File.Exists(recentFile));
            Assert.True(File.Exists(Path.Combine(nestedDirectory, "old.cache")));
        }
        finally
        {
            if (Directory.Exists(isolatedTemp)) Directory.Delete(isolatedTemp, recursive: true);
        }
    }
}

