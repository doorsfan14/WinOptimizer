using WinOptimizer.Models;

namespace WinOptimizer.Services;

public static class MetricsMath
{
    public static double Percent(double value, double total)
    {
        if (!double.IsFinite(value) || !double.IsFinite(total) || total <= 0)
            return 0;
        return Math.Clamp(value / total * 100d, 0d, 100d);
    }

    public static double? ValidatePercent(double? value)
    {
        if (value is null || !double.IsFinite(value.Value) || value < 0 || value > 100)
            return null;
        return value;
    }

    public static ulong SafeDifference(ulong total, ulong available) => total >= available ? total - available : 0;

    public static string FormatBytes(ulong bytes)
    {
        string[] units = ["B", "KB", "GB", "TB", "PB"];
        double amount = bytes;
        var unit = 0;
        while (amount >= 1024 && unit < units.Length - 1)
        {
            amount /= 1024;
            unit++;
        }
        return unit == 0 ? $"{amount:0} {units[unit]}" : $"{amount:0.0} {units[unit]}";
    }

    public static string CpuStatus(double? utilization)
    {
        if (utilization is null) return "Collecting a second sample";
        return utilization.Value switch
        {
            >= 90 => "The processor is working hard right now",
            >= 70 => "The processor is busy with current work",
            _ => "The processor has room for more work"
        };
    }

    public static string MemoryStatus(MemoryReading? memory)
    {
        if (memory is null) return "Memory measurements are not ready yet";
        var physicalPressure = memory.AvailableBytes < Math.Max(512UL * 1024 * 1024, memory.UsableBytes / 10);
        return physicalPressure && memory.UsedPercent >= 90
            ? "Available memory is low; check whether the PC feels slow"
            : "Windows still has physical memory available";
    }

    public static double CpuBusyPercentage(ulong idleDelta, ulong kernelDelta, ulong userDelta)
    {
        var total = (double)kernelDelta + userDelta;
        if (total <= 0) return 0;
        return Math.Clamp((total - idleDelta) / total * 100d, 0d, 100d);
    }
}

public sealed class HistoryBuffer<T>
{
    private readonly Queue<T> _items = new();
    public int Capacity { get; }
    public int Count => _items.Count;
    public IReadOnlyCollection<T> Items => _items;

    public HistoryBuffer(int capacity)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        Capacity = capacity;
    }

    public void Add(T item)
    {
        _items.Enqueue(item);
        while (_items.Count > Capacity) _items.Dequeue();
    }
}
