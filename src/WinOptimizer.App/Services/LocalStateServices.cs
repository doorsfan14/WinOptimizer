using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Media;
using WinOptimizer.Models;

namespace WinOptimizer.Services;

public sealed class SettingsStore
{
    private readonly string _path = Path.Combine(AppDataDirectory, "settings.json");
    private static readonly JsonSerializerOptions JsonOptions = CreateOptions();

    public static string AppDataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinOptimizer");

    public UserPreferences Load()
    {
        try
        {
            if (!File.Exists(_path)) return new UserPreferences();
            var loaded = JsonSerializer.Deserialize<UserPreferences>(File.ReadAllText(_path), JsonOptions) ?? new UserPreferences();
            loaded.PollIntervalSeconds = Math.Clamp(loaded.PollIntervalSeconds, 1, 10);
            loaded.HistoryDurationMinutes = Math.Clamp(loaded.HistoryDurationMinutes, 1, 60);
            return loaded;
        }
        catch (Exception ex)
        {
            AppLog.Write("Settings could not be read; safe defaults were loaded.", ex);
            return new UserPreferences();
        }
    }

    public async Task SaveAsync(UserPreferences preferences, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(AppDataDirectory);
        var temporaryPath = _path + ".tmp";
        var json = JsonSerializer.Serialize(preferences, JsonOptions);
        await File.WriteAllTextAsync(temporaryPath, json, cancellationToken);
        File.Move(temporaryPath, _path, true);
    }

    internal static JsonSerializerOptions CreateOptions() => new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
}

public sealed class ActivityLog
{
    private readonly string _path = Path.Combine(SettingsStore.AppDataDirectory, "history.json");
    private readonly SemaphoreSlim _gate = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = SettingsStore.CreateOptions();

    public async Task<IReadOnlyList<ActivityEntry>> ReadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_path)) return [];
            return JsonSerializer.Deserialize<List<ActivityEntry>>(await File.ReadAllTextAsync(_path, cancellationToken), JsonOptions)
                ?.OrderByDescending(item => item.Timestamp).ToArray() ?? [];
        }
        catch (Exception ex)
        {
            AppLog.Write("Local activity history could not be read.", ex);
            return [];
        }
        finally { _gate.Release(); }
    }

    public async Task AddAsync(ActivityEntry entry, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(SettingsStore.AppDataDirectory);
            var entries = File.Exists(_path)
                ? JsonSerializer.Deserialize<List<ActivityEntry>>(await File.ReadAllTextAsync(_path, cancellationToken), JsonOptions) ?? []
                : [];
            entries.Add(entry);
            var json = JsonSerializer.Serialize(entries.OrderByDescending(item => item.Timestamp).Take(300).ToArray(), JsonOptions);
            var temporaryPath = _path + ".tmp";
            await File.WriteAllTextAsync(temporaryPath, json, cancellationToken);
            File.Move(temporaryPath, _path, true);
        }
        finally { _gate.Release(); }
    }
}

public static class ThemeService
{
    private static readonly IReadOnlyDictionary<string, (string Dark, string Light)> Palette =
        new Dictionary<string, (string, string)>
        {
            ["AppBackground"] = ("#0A1115", "#F0F4F3"),
            ["SidebarBackground"] = ("#0E171C", "#E7EDEB"),
            ["Surface"] = ("#131E24", "#FAFCFB"),
            ["SurfaceRaised"] = ("#19262D", "#FFFFFF"),
            ["Line"] = ("#293941", "#D7E1DE"),
            ["TextPrimary"] = ("#F0F6F5", "#1B2A2C"),
            ["TextMuted"] = ("#94A8B0", "#607478"),
            ["Accent"] = ("#77D8CA", "#087F73"),
            ["AccentSoft"] = ("#213D3D", "#DDF1ED"),
            ["Blue"] = ("#87B7F7", "#356FB2"),
            ["Warning"] = ("#EABF78", "#986416"),
            ["Danger"] = ("#EF8F8F", "#AB4141"),
            ["Purple"] = ("#B7A5F2", "#634DA5"),
            ["ChartGrid"] = ("#28363D", "#DCE5E3"),
            ["SidebarActive"] = ("#1A3436", "#D9ECE8"),
            ["Overlay"] = ("#B70A1115", "#B7F0F4F3")
        };

    public static event Action? ThemeChanged;

    public static void Apply(ThemeChoice choice)
    {
        var dark = choice == ThemeChoice.Dark;
        if (choice == ThemeChoice.System)
        {
            try
            {
                var useLightTheme = Microsoft.Win32.Registry.GetValue(
                    @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                    "AppsUseLightTheme", 1);
                dark = Convert.ToInt32(useLightTheme) == 0;
            }
            catch { dark = false; }
        }
        foreach (var item in Palette)
            Application.Current.Resources[item.Key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark ? item.Value.Dark : item.Value.Light));
        ThemeChanged?.Invoke();
    }
}

public static class AppLog
{
    private static readonly object Sync = new();
    private static readonly string PathName = Path.Combine(SettingsStore.AppDataDirectory, "winoptimizer.log");

    public static void Write(string message, Exception? exception = null)
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(SettingsStore.AppDataDirectory);
                var info = exception is null ? string.Empty : $" {exception.GetType().Name}: {exception.Message}";
                File.AppendAllText(PathName, $"{DateTimeOffset.Now:O} {message}{info}{Environment.NewLine}");
                var file = new FileInfo(PathName);
                if (file.Length > 2 * 1024 * 1024)
                {
                    var previous = PathName + ".1";
                    File.Delete(previous);
                    File.Move(PathName, previous);
                }
            }
        }
        catch { /* Logging must never interrupt monitoring or an undo operation. */ }
    }
}
