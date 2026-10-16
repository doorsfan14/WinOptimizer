using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using WinOptimizer.Models;

namespace WinOptimizer.Services;

public sealed record PowerPlanStatus(
    Guid? ActivePlanId,
    string? ActivePlanName,
    bool HighPerformancePlanAvailable,
    string? HighPerformancePlanName,
    bool IsOnAcPower);

public sealed record PowerPlanChangeResult(
    bool Succeeded,
    string Message,
    Guid? PreviousPlanId = null,
    Guid? AppliedPlanId = null,
    bool IsConflict = false);

/// <summary>Reads and changes the Windows active power scheme through PowrProf.</summary>
public sealed class PowerPlanService
{
    public static readonly Guid HighPerformanceSchemeId = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");

    public PowerPlanStatus ReadStatus()
    {
        var active = ReadActiveScheme();
        return new PowerPlanStatus(active?.Id, active?.Name,
            TryReadFriendlyName(HighPerformanceSchemeId, out var highName), highName, IsOnAcPower());
    }

    public PowerPlanChangeResult ApplyHighPerformance()
    {
        var status = ReadStatus();
        if (!status.IsOnAcPower)
            return new(false, "Keep the current power plan while the PC is on battery or its power source is unknown.");
        if (!status.HighPerformancePlanAvailable)
            return new(false, "Windows does not expose the built-in High performance plan on this PC.");
        if (status.ActivePlanId is not { } previous)
            return new(false, "Windows could not identify the active power plan, so no change was made.");
        if (previous == HighPerformanceSchemeId)
            return new(false, "The High performance plan is already active.", previous, HighPerformanceSchemeId);

        var applied = HighPerformanceSchemeId;
        var result = PowerSetActiveScheme(IntPtr.Zero, ref applied);
        if (result != 0)
            return new(false, $"Windows could not change the power plan (error {result}).", previous, applied);

        var verified = ReadActiveScheme();
        return verified?.Id == applied
            ? new(true, $"Windows confirmed {status.HighPerformancePlanName ?? "High performance"} is active on AC power.", previous, applied)
            : new(false, "The request returned, but Windows did not report the new plan as active.", previous, applied);
    }

    public PowerPlanChangeResult Restore(Guid previousPlanId, Guid expectedCurrentPlanId)
    {
        var current = ReadActiveScheme();
        if (current?.Id != expectedCurrentPlanId)
            return new(false, "The active plan changed after this WinOptimizer action. Nothing was overwritten.", previousPlanId, expectedCurrentPlanId, true);
        if (!TryReadFriendlyName(previousPlanId, out var previousName))
            return new(false, "The original power plan is no longer available, so it could not be restored.", previousPlanId, expectedCurrentPlanId);

        var target = previousPlanId;
        var result = PowerSetActiveScheme(IntPtr.Zero, ref target);
        if (result != 0)
            return new(false, $"Windows could not restore {previousName} (error {result}).", previousPlanId, expectedCurrentPlanId);

        return ReadActiveScheme()?.Id == target
            ? new(true, $"Windows confirmed {previousName} is active again.", previousPlanId, target)
            : new(false, "Windows did not confirm the previous plan after restoration.", previousPlanId, expectedCurrentPlanId);
    }

    private static (Guid Id, string Name)? ReadActiveScheme()
    {
        IntPtr schemePointer = IntPtr.Zero;
        try
        {
            if (PowerGetActiveScheme(IntPtr.Zero, out schemePointer) != 0 || schemePointer == IntPtr.Zero) return null;
            var id = Marshal.PtrToStructure<Guid>(schemePointer);
            return TryReadFriendlyName(id, out var name) ? (id, name) : (id, id.ToString("D"));
        }
        catch (DllNotFoundException) { return null; }
        catch (EntryPointNotFoundException) { return null; }
        finally { if (schemePointer != IntPtr.Zero) LocalFree(schemePointer); }
    }

    private static bool TryReadFriendlyName(Guid scheme, out string name)
    {
        name = string.Empty;
        try
        {
            var subgroup = Guid.Empty;
            var setting = Guid.Empty;
            uint size = 0;
            var status = PowerReadFriendlyName(IntPtr.Zero, ref scheme, ref subgroup, ref setting, ref size, IntPtr.Zero);
            if (status != 0 || size == 0) return false;
            var buffer = Marshal.AllocHGlobal((int)size);
            try
            {
                if (PowerReadFriendlyName(IntPtr.Zero, ref scheme, ref subgroup, ref setting, ref size, buffer) != 0) return false;
                name = Marshal.PtrToStringUni(buffer)?.TrimEnd('\0') ?? string.Empty;
                return !string.IsNullOrWhiteSpace(name);
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        catch (DllNotFoundException) { return false; }
        catch (EntryPointNotFoundException) { return false; }
    }

    private static bool IsOnAcPower()
    {
        try { var status = new SystemPowerStatus(); return GetSystemPowerStatus(ref status) && status.AcLineStatus == 1; }
        catch (DllNotFoundException) { return false; }
        catch (EntryPointNotFoundException) { return false; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte AcLineStatus;
        public byte BatteryFlag;
        public byte BatteryPercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    [DllImport("powrprof.dll")] private static extern uint PowerGetActiveScheme(IntPtr userRootPowerKey, out IntPtr activePolicyGuid);
    [DllImport("powrprof.dll", CharSet = CharSet.Unicode)] private static extern uint PowerReadFriendlyName(IntPtr userRootPowerKey, ref Guid schemeGuid, ref Guid subgroupOfPowerSettingsGuid, ref Guid powerSettingGuid, ref uint bufferSize, IntPtr buffer);
    [DllImport("powrprof.dll")] private static extern uint PowerSetActiveScheme(IntPtr userRootPowerKey, ref Guid schemeGuid);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetSystemPowerStatus(ref SystemPowerStatus systemPowerStatus);
}

public sealed record DriveMaintenanceResult(bool Succeeded, string Message, int? ExitCode = null, bool UserCancelled = false);

/// <summary>Starts Windows' media-aware /O optimization for one already-discovered fixed volume.</summary>
public sealed class DriveMaintenanceService
{
    public static string? NormalizeVolume(string? mountPoint)
    {
        if (mountPoint is null || mountPoint.Length is < 2 or > 3 || !char.IsAsciiLetter(mountPoint[0]) || mountPoint[1] != ':') return null;
        if (mountPoint.Length == 3 && mountPoint[2] != '\\' && mountPoint[2] != '/') return null;
        return char.ToUpperInvariant(mountPoint[0]) + ":";
    }

    public async Task<DriveMaintenanceResult> RunAsync(string mountPoint)
    {
        var volume = NormalizeVolume(mountPoint);
        if (volume is null) return new(false, "Choose a local drive letter reported by Windows.");
        var executable = Path.Combine(Environment.SystemDirectory, "defrag.exe");
        if (!File.Exists(executable)) return new(false, "Windows drive optimization is unavailable on this PC.");

        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Normal
        };
        startInfo.ArgumentList.Add(volume);
        startInfo.ArgumentList.Add("/O");
        startInfo.ArgumentList.Add("/U");
        startInfo.ArgumentList.Add("/V");

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null) return new(false, "Windows did not start the drive optimization task.");
            await process.WaitForExitAsync();
            return process.ExitCode == 0
                ? new(true, $"Windows completed its media-aware operation for {volume}. Review the Windows report for its result.", process.ExitCode)
                : new(false, $"Windows drive optimization ended with exit code {process.ExitCode}.", process.ExitCode);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return new(false, "The administrator prompt was declined. No drive operation was started.", UserCancelled: true);
        }
        catch (Win32Exception ex)
        {
            AppLog.Write("Windows drive optimization could not be started.", ex);
            return new(false, "Windows could not start drive optimization. Check the local log for details.");
        }
        catch (InvalidOperationException ex)
        {
            AppLog.Write("Windows drive optimization could not be started.", ex);
            return new(false, "Windows could not start drive optimization. Check the local log for details.");
        }
    }
}

