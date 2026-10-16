using System.IO;
using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.Principal;
using LibreHardwareMonitor.Hardware;
using WinOptimizer.Models;

namespace WinOptimizer.Services;

public interface ITelemetryProvider : IDisposable
{
    Task<SystemSnapshot> SampleAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProcessUsage>> GetProcessesAsync(CancellationToken cancellationToken = default);
    Task<SystemCapabilities> DetectCapabilitiesAsync(CancellationToken cancellationToken = default);
}

public sealed record SystemCapabilities(
    string WindowsVersion,
    string Architecture,
    bool IsAdministrator,
    string? CpuName,
    int LogicalProcessors,
    IReadOnlyList<string> GpuNames,
    string SensorSummary);

/// <summary>Reads Windows counters and capability-checked LibreHardwareMonitor sensors.</summary>
public sealed class WindowsTelemetryProvider : ITelemetryProvider
{
    private static readonly TimeSpan HardwareSensorRefreshInterval = TimeSpan.FromSeconds(5);
    private readonly object _sync = new();
    private readonly ProcessUsageSampler _processSampler = new();
    private readonly DiskCounter _diskCounter = new();
    private Computer? _computer;
    private bool _sensorInitAttempted;
    private string? _cpuName;
    private IReadOnlyList<string> _gpuNames = [];
    private IReadOnlyDictionary<string, string> _driveModels = new Dictionary<string, string>();
    private ulong _lastIdle;
    private ulong _lastKernel;
    private ulong _lastUser;
    private bool _haveCpuBaseline;
    private DateTimeOffset _lastPowerPlanRead;
    private string? _powerPlan;
    private SensorReadingSet? _cachedHardwareReadings;
    private DateTimeOffset? _lastHardwareSensorRead;
    private bool _disposed;

    public Task<SystemSnapshot> SampleAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => SampleCore(cancellationToken), cancellationToken);

    public Task<IReadOnlyList<ProcessUsage>> GetProcessesAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => _processSampler.Read(), cancellationToken);

    public Task<SystemCapabilities> DetectCapabilitiesAsync(CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        EnsureHardwareMonitor();
        var isAdmin = false;
        try { isAdmin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator); }
        catch { }
        return new SystemCapabilities(
            Environment.OSVersion.VersionString,
            Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit",
            isAdmin,
            _cpuName,
            Environment.ProcessorCount,
            _gpuNames,
            _computer?.Hardware.Count > 0
                ? "LibreHardwareMonitor initialized; sensor availability depends on firmware and device support."
                : "LibreHardwareMonitor did not expose hardware sensors on this system.");
    }, cancellationToken);

    private SystemSnapshot SampleCore(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureHardwareMonitor();

        double? cpuUsage = null;
        if (ReadSystemTimes(out var idle, out var kernel, out var user))
        {
            if (_haveCpuBaseline)
            {
                var idleDelta = idle - _lastIdle;
                var kernelDelta = kernel - _lastKernel;
                var userDelta = user - _lastUser;
                cpuUsage = MetricsMath.CpuBusyPercentage(idleDelta, kernelDelta, userDelta);
            }
            _lastIdle = idle;
            _lastKernel = kernel;
            _lastUser = user;
            _haveCpuBaseline = true;
        }

        var memory = ReadMemory();
        var devices = ReadHardwareSensorsAtInterval();
        var drives = ReadDrives();
        if (DateTimeOffset.UtcNow - _lastPowerPlanRead > TimeSpan.FromSeconds(30))
        {
            _powerPlan = PowerPlanReader.Read();
            _lastPowerPlanRead = DateTimeOffset.UtcNow;
        }

        return new SystemSnapshot(
            DateTimeOffset.Now,
            Environment.MachineName,
            Environment.OSVersion.VersionString,
            Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit",
            Environment.ProcessorCount,
            _cpuName,
            MetricsMath.ValidatePercent(cpuUsage),
            devices.CpuFrequency,
            devices.CpuTemperature,
            devices.CpuTemperatureSource,
            memory,
            devices.Gpus,
            devices.Fans,
            drives,
            _diskCounter.ReadPercent(),
            _powerPlan,
            devices.State,
            devices.Message);
    }

    private void EnsureHardwareMonitor()
    {
        if (_sensorInitAttempted) return;
        lock (_sync)
        {
            if (_sensorInitAttempted) return;
            _sensorInitAttempted = true;
            try
            {
                _cpuName = QueryFirst("SELECT Name FROM Win32_Processor", "Name");
                _gpuNames = QueryMany("SELECT Name FROM Win32_VideoController", "Name");
                _driveModels = QueryDriveModels();
                _computer = new Computer
                {
                    IsCpuEnabled = true,
                    IsGpuEnabled = true,
                    IsMotherboardEnabled = true,
                    IsControllerEnabled = false,
                    IsMemoryEnabled = false,
                    IsNetworkEnabled = false,
                    IsStorageEnabled = false,
                    IsBatteryEnabled = false,
                    IsPowerMonitorEnabled = false,
                    IsPsuEnabled = false
                };
                _computer.Open();
            }
            catch (Exception ex)
            {
                _computer?.Close();
                _computer = null;
                _initError = ex is UnauthorizedAccessException
                    ? "Hardware sensors could not be opened with this account. Windows resource readings remain available."
                    : "The hardware sensor interface is unavailable. Windows resource readings remain available.";
            }
        }
    }

    private string? _initError;

    private SensorReadingSet ReadHardwareSensors()
    {
        if (_computer is null)
            return new SensorReadingSet([], [], null, null, null, ReadingState.Unavailable,
                _initError ?? "No supported hardware sensor interface is available.");

        try
        {
            var allHardware = new List<IHardware>();
            foreach (var hardware in _computer.Hardware)
                UpdateTree(hardware, allHardware);

            var cpu = allHardware.Where(h => h.HardwareType == HardwareType.Cpu).ToArray();
            var cpuSensors = cpu.SelectMany(h => h.Sensors.Select(s => (Hardware: h, Sensor: s))).ToArray();
            var temperature = cpuSensors
                .Where(x => x.Sensor.SensorType == SensorType.Temperature && IsValid(x.Sensor.Value))
                .OrderByDescending(x => TemperaturePriority(x.Sensor.Name))
                .FirstOrDefault();
            var clock = cpuSensors
                .Where(x => x.Sensor.SensorType == SensorType.Clock && IsValid(x.Sensor.Value))
                .OrderByDescending(x => x.Sensor.Name.Contains("average", StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault();

            var gpuHardware = allHardware.Where(h => h.HardwareType is HardwareType.GpuAmd or HardwareType.GpuIntel or HardwareType.GpuNvidia).ToArray();
            var gpus = gpuHardware.Select(BuildGpu).ToList();
            for (var index = 0; index < _gpuNames.Count; index++)
            {
                var name = _gpuNames[index];
                if (!gpus.Any(g => g.Name.Contains(name, StringComparison.OrdinalIgnoreCase) || name.Contains(g.Name, StringComparison.OrdinalIgnoreCase)))
                    gpus.Add(new GpuReading($"windows-gpu-{index}", name, null, null, null, null, null, null, null));
            }
            var fans = allHardware.SelectMany(h => h.Sensors
                    .Where(s => s.SensorType == SensorType.Fan)
                    .Select(s => BuildFan(h, s)))
                .Where(f => f.State is FanState.Available or FanState.ZeroRpm)
                .ToArray();
            var hasAnySensor = allHardware.Any(h => h.Sensors.Any(s => s.Value is not null));
            return new SensorReadingSet(
                gpus.ToArray(),
                fans,
                IsValid(clock.Sensor?.Value) ? clock.Sensor!.Value!.Value : null,
                IsValid(temperature.Sensor?.Value) ? temperature.Sensor!.Value!.Value : null,
                temperature.Sensor is null ? null : $"{temperature.Hardware.Name} · {temperature.Sensor.Name}",
                hasAnySensor ? ReadingState.Available : ReadingState.Unavailable,
                hasAnySensor
                    ? "Sensor values are reported by LibreHardwareMonitor. Unsupported readings are left blank."
                    : "The hardware interface opened, but no usable sensor values were reported.");
        }
        catch
        {
            return new SensorReadingSet([], [], null, null, null, ReadingState.Error,
                "A hardware sensor could not be read. Windows resource measurements are still available.");
        }
    }

    private SensorReadingSet ReadHardwareSensorsAtInterval()
    {
        var now = DateTimeOffset.UtcNow;
        if (_cachedHardwareReadings is null || SensorRefreshPolicy.ShouldRefresh(_lastHardwareSensorRead, now, HardwareSensorRefreshInterval))
        {
            _cachedHardwareReadings = ReadHardwareSensors();
            _lastHardwareSensorRead = now;
        }
        return _cachedHardwareReadings!;
    }

    private static void UpdateTree(IHardware hardware, ICollection<IHardware> all)
    {
        hardware.Update();
        all.Add(hardware);
        foreach (var child in hardware.SubHardware)
            UpdateTree(child, all);
    }

    private static GpuReading BuildGpu(IHardware hardware)
    {
        var sensors = hardware.Sensors;
        var load = sensors.Where(s => s.SensorType == SensorType.Load && IsValid(s.Value))
            .OrderByDescending(s => s.Name.Contains("core", StringComparison.OrdinalIgnoreCase) || s.Name.Contains("3d", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault();
        var temp = sensors.Where(s => s.SensorType == SensorType.Temperature && IsValid(s.Value)).FirstOrDefault();
        var clock = sensors.Where(s => s.SensorType == SensorType.Clock && IsValid(s.Value))
            .OrderByDescending(s => s.Name.Contains("core", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault();
        var memoryUsed = sensors.Where(s => s.Name.Contains("memory used", StringComparison.OrdinalIgnoreCase) && IsValid(s.Value)).FirstOrDefault();
        var memoryTotal = sensors.Where(s => s.Name.Contains("memory total", StringComparison.OrdinalIgnoreCase) && IsValid(s.Value)).FirstOrDefault();
        return new GpuReading(
            hardware.Identifier.ToString(), hardware.Name,
            MetricsMath.ValidatePercent(load?.Value), ToBytes(memoryUsed), ToBytes(memoryTotal),
            IsValid(temp?.Value) ? temp!.Value!.Value : null,
            IsValid(clock?.Value) ? clock!.Value!.Value : null,
            temp is null ? null : $"{hardware.Name} · {temp.Name}",
            load is null ? null : $"{hardware.Name} · {load.Name}");
    }

    private static FanReading BuildFan(IHardware hardware, ISensor sensor)
    {
        var rpm = IsValid(sensor.Value) && sensor.Value >= 0 ? sensor.Value.Value : (double?)null;
        return new FanReading(
            sensor.Identifier.ToString(), sensor.Name,
            hardware.Name,
            rpm,
            rpm is null ? FanState.Unavailable : rpm <= 0 ? FanState.ZeroRpm : FanState.Available,
            $"LibreHardwareMonitor · {hardware.Name}");
    }

    private static int TemperaturePriority(string name)
    {
        if (name.Contains("package", StringComparison.OrdinalIgnoreCase)) return 4;
        if (name.Contains("tctl", StringComparison.OrdinalIgnoreCase) || name.Contains("tdie", StringComparison.OrdinalIgnoreCase)) return 3;
        if (name.Contains("cpu", StringComparison.OrdinalIgnoreCase)) return 2;
        if (name.Contains("core", StringComparison.OrdinalIgnoreCase)) return 1;
        return 0;
    }

    private static ulong? ToBytes(ISensor? sensor)
    {
        if (!IsValid(sensor?.Value)) return null;
        var factor = sensor!.SensorType switch
        {
            SensorType.SmallData => 1024d * 1024,
            SensorType.Data => 1024d * 1024 * 1024,
            _ => 1d
        };
        var bytes = sensor.Value!.Value * factor;
        return bytes >= 0 && bytes <= ulong.MaxValue ? (ulong)bytes : null;
    }

    private static bool IsValid(float? value) => value is { } v && float.IsFinite(v);

    private static MemoryReading? ReadMemory()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (!GlobalMemoryStatusEx(ref status)) return null;
        return new MemoryReading(
            status.TotalPhysical,
            Math.Min(status.AvailablePhysical, status.TotalPhysical),
            MetricsMath.SafeDifference(status.TotalPageFile, status.AvailablePageFile),
            status.TotalPageFile,
            TryReadInstalledMemory());
    }

    private static ulong? TryReadInstalledMemory()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem");
            using var results = searcher.Get();
            foreach (ManagementObject entry in results)
            {
                using (entry)
                    if (entry["TotalPhysicalMemory"] is not null && ulong.TryParse(entry["TotalPhysicalMemory"].ToString(), out var bytes))
                        return bytes;
            }
        }
        catch { }
        return null;
    }

    private IReadOnlyList<StorageDriveReading> ReadDrives()
    {
        var drives = new List<StorageDriveReading>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (drive.DriveType != DriveType.Fixed || !drive.IsReady) continue;
                var mount = drive.RootDirectory.FullName;
                var key = Path.GetPathRoot(mount)?.TrimEnd(Path.DirectorySeparatorChar) ?? mount;
                _driveModels.TryGetValue(key, out var model);
                drives.Add(new StorageDriveReading(
                    drive.Name.TrimEnd(Path.DirectorySeparatorChar), mount, drive.DriveFormat,
                    model, null, (ulong)drive.TotalSize, (ulong)drive.AvailableFreeSpace));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return drives;
    }

    private static string? QueryFirst(string query, string property)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(query);
            using var results = searcher.Get();
            foreach (ManagementObject item in results)
            {
                using (item) return item[property]?.ToString()?.Trim();
            }
        }
        catch { }
        return null;
    }

    private static IReadOnlyList<string> QueryMany(string query, string property)
    {
        var values = new List<string>();
        try
        {
            using var searcher = new ManagementObjectSearcher(query);
            using var results = searcher.Get();
            foreach (ManagementObject item in results)
            {
                using (item)
                {
                    var value = item[property]?.ToString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(value)) values.Add(value);
                }
            }
        }
        catch { }
        return values.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IReadOnlyDictionary<string, string> QueryDriveModels()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var logicalSearcher = new ManagementObjectSearcher("SELECT DeviceID FROM Win32_LogicalDisk WHERE DriveType = 3");
            using var logicals = logicalSearcher.Get();
            foreach (ManagementObject logical in logicals)
            {
                using (logical)
                {
                    var drive = logical["DeviceID"]?.ToString();
                    if (string.IsNullOrWhiteSpace(drive)) continue;
                    foreach (ManagementObject partition in logical.GetRelated("Win32_DiskPartition"))
                    {
                        using (partition)
                        {
                            foreach (ManagementObject disk in partition.GetRelated("Win32_DiskDrive"))
                            {
                                using (disk)
                                {
                                    var model = disk["Model"]?.ToString()?.Trim();
                                    if (!string.IsNullOrWhiteSpace(model)) result[drive] = model;
                                }
                            }
                        }
                    }
                }
            }
        }
        catch { }
        return result;
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            try { _computer?.Close(); } catch { }
            _computer = null;
            _diskCounter.Dispose();
        }
    }

    private sealed record SensorReadingSet(
        IReadOnlyList<GpuReading> Gpus,
        IReadOnlyList<FanReading> Fans,
        double? CpuFrequency,
        double? CpuTemperature,
        string? CpuTemperatureSource,
        ReadingState State,
        string Message);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx lpBuffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime { public uint Low; public uint High; public ulong Value => ((ulong)High << 32) | Low; }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out FileTime idleTime, out FileTime kernelTime, out FileTime userTime);

    private static bool ReadSystemTimes(out ulong idle, out ulong kernel, out ulong user)
    {
        var success = GetSystemTimes(out FileTime idleTime, out FileTime kernelTime, out FileTime userTime);
        idle = idleTime.Value;
        kernel = kernelTime.Value;
        user = userTime.Value;
        return success;
    }
}

internal static class SensorRefreshPolicy
{
    public static bool ShouldRefresh(DateTimeOffset? lastRead, DateTimeOffset now, TimeSpan interval) =>
        lastRead is null || interval <= TimeSpan.Zero || now - lastRead.Value >= interval;
}

internal sealed class ProcessUsageSampler
{
    private readonly Dictionary<int, (TimeSpan Cpu, DateTimeOffset At)> _previous = [];

    public IReadOnlyList<ProcessUsage> Read()
    {
        var now = DateTimeOffset.UtcNow;
        var result = new List<ProcessUsage>();
        var next = new Dictionary<int, (TimeSpan Cpu, DateTimeOffset At)>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    var id = process.Id;
                    var cpu = process.TotalProcessorTime;
                    var workingSet = (ulong)Math.Max(0, process.WorkingSet64);
                    var name = process.ProcessName;
                    double? percent = null;
                    if (_previous.TryGetValue(id, out var previous))
                    {
                        var seconds = (now - previous.At).TotalSeconds;
                        if (seconds > 0 && cpu >= previous.Cpu)
                            percent = Math.Clamp((cpu - previous.Cpu).TotalSeconds / seconds / Environment.ProcessorCount * 100d, 0d, 100d);
                    }
                    next[id] = (cpu, now);
                    result.Add(new ProcessUsage(id, name, percent, workingSet, null));
                }
                catch { }
            }
        }
        _previous.Clear();
        foreach (var pair in next) _previous[pair.Key] = pair.Value;
        return result.OrderByDescending(p => p.CpuPercent).ThenByDescending(p => p.WorkingSetBytes).Take(80).ToArray();
    }
}

internal sealed class DiskCounter : IDisposable
{
    private IntPtr _query;
    private IntPtr _counter;
    private bool _initialized;

    public DiskCounter()
    {
        try
        {
            if (PdhOpenQuery(null, IntPtr.Zero, out _query) != 0) return;
            if (PdhAddEnglishCounter(_query, @"\PhysicalDisk(_Total)\% Disk Time", IntPtr.Zero, out _counter) != 0) return;
            PdhCollectQueryData(_query); // Establish the first sample; the next one contains a rate.
            _initialized = true;
        }
        catch { }
    }

    public double? ReadPercent()
    {
        if (!_initialized || _query == IntPtr.Zero || _counter == IntPtr.Zero) return null;
        try
        {
            if (PdhCollectQueryData(_query) != 0) return null;
            if (PdhGetFormattedCounterValue(_counter, 0x00000200, out _, out var value) != 0 || value.Status != 0)
                return null;
            return double.IsFinite(value.Value) ? Math.Clamp(value.Value, 0, 100) : null;
        }
        catch { return null; }
    }

    public void Dispose()
    {
        if (_counter != IntPtr.Zero) PdhRemoveCounter(_counter);
        if (_query != IntPtr.Zero) PdhCloseQuery(_query);
        _counter = IntPtr.Zero;
        _query = IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct FormattedValue
    {
        [FieldOffset(0)] public int Status;
        [FieldOffset(8)] public double Value;
    }

    [DllImport("pdh.dll", EntryPoint = "PdhOpenQueryW", CharSet = CharSet.Unicode)] private static extern int PdhOpenQuery(string? source, IntPtr userData, out IntPtr query);
    [DllImport("pdh.dll", EntryPoint = "PdhAddEnglishCounterW", CharSet = CharSet.Unicode)] private static extern int PdhAddEnglishCounter(IntPtr query, string path, IntPtr userData, out IntPtr counter);
    [DllImport("pdh.dll")] private static extern int PdhCollectQueryData(IntPtr query);
    [DllImport("pdh.dll")] private static extern int PdhGetFormattedCounterValue(IntPtr counter, uint format, out uint type, out FormattedValue value);
    [DllImport("pdh.dll")] private static extern int PdhRemoveCounter(IntPtr counter);
    [DllImport("pdh.dll")] private static extern int PdhCloseQuery(IntPtr query);
}

internal static class PowerPlanReader
{
    public static string? Read() => new PowerPlanService().ReadStatus().ActivePlanName;
}

