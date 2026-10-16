using WinOptimizer.Models;
using WinOptimizer.Services;
using Xunit;

namespace WinOptimizer.Tests;

public sealed class OptimizationActionTests
{
    [Fact]
    public void Performance_plan_recommendation_is_only_eligible_on_confirmed_ac_power()
    {
        var engine = new RecommendationEngine();
        var activePlan = Guid.NewGuid();

        var onBattery = engine.Evaluate(new RecommendationContext(SystemSnapshot.Empty, 0, 0, 0,
            HighPerformancePlanAvailable: true, IsOnAcPower: false, ActivePowerPlanId: activePlan));
        var onAc = engine.Evaluate(new RecommendationContext(SystemSnapshot.Empty, 0, 0, 0,
            HighPerformancePlanAvailable: true, IsOnAcPower: true, ActivePowerPlanId: activePlan));
        var alreadyActive = engine.Evaluate(new RecommendationContext(SystemSnapshot.Empty, 0, 0, 0,
            HighPerformancePlanAvailable: true, IsOnAcPower: true,
            ActivePowerPlanId: PowerPlanService.HighPerformanceSchemeId));

        Assert.DoesNotContain(onBattery, item => item.Id == "power.performance");
        Assert.Contains(onAc, item => item.Id == "power.performance" && item.HasAction && item.Risk == RecommendationRisk.Moderate);
        Assert.Contains(alreadyActive, item => item.Id == "power.performance" && item.State == RecommendationState.AlreadyApplied && !item.HasAction);
    }

    [Theory]
    [InlineData("C:", "C:")]
    [InlineData("c:\\", "C:")]
    [InlineData("Z:/", "Z:")]
    public void Drive_maintenance_normalizes_only_drive_roots(string? input, string expected)
    {
        Assert.Equal(expected, DriveMaintenanceService.NormalizeVolume(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("C:\\Windows")]
    [InlineData("..\\C:")]
    [InlineData("\\\\server\\share")]
    [InlineData("C; /s")]
    public void Drive_maintenance_rejects_paths_and_untrusted_arguments(string? input)
    {
        Assert.Null(DriveMaintenanceService.NormalizeVolume(input));
    }

    [Fact]
    public void Hardware_sensors_are_refreshed_on_a_bounded_interval()
    {
        var now = DateTimeOffset.UtcNow;
        var interval = TimeSpan.FromSeconds(5);

        Assert.True(SensorRefreshPolicy.ShouldRefresh(null, now, interval));
        Assert.False(SensorRefreshPolicy.ShouldRefresh(now, now.AddSeconds(4.9), interval));
        Assert.True(SensorRefreshPolicy.ShouldRefresh(now, now.AddSeconds(5), interval));
        Assert.True(SensorRefreshPolicy.ShouldRefresh(now, now, TimeSpan.Zero));
    }
}

