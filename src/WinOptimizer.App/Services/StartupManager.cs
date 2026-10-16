using System.IO;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using WinOptimizer.Models;

namespace WinOptimizer.Services;

public sealed record StartupMutationResult(bool Succeeded, string Message, string? BackupPath = null);

public sealed class StartupManager
{
    private readonly string _backupDirectory = Path.Combine(SettingsStore.AppDataDirectory, "StartupBackups");
    private static readonly string[] RunKeyPaths =
    [
        @"Software\Microsoft\Windows\CurrentVersion\Run",
        @"Software\Microsoft\Windows\CurrentVersion\RunOnce"
    ];

    public Task<IReadOnlyList<StartupItem>> ScanAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => Scan(cancellationToken), cancellationToken);

    public IReadOnlyList<StartupItem> Scan(CancellationToken cancellationToken = default)
    {
        var items = new List<StartupItem>();
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        {
            foreach (var path in RunKeyPaths)
            {
                foreach (var view in Environment.Is64BitOperatingSystem
                             ? new[] { RegistryView.Registry64, RegistryView.Registry32 }
                             : new[] { RegistryView.Default })
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                        using var key = baseKey.OpenSubKey(path, writable: false);
                        if (key is null) continue;
                        foreach (var name in key.GetValueNames())
                        {
                            var command = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames)?.ToString();
                            if (string.IsNullOrWhiteSpace(command)) continue;
                            var id = MakeId(hive, view, path, name);
                            var protectedEntry = IsSensitive(name, command);
                            var isCurrentUserRun = hive == RegistryHive.CurrentUser &&
                                string.Equals(path, RunKeyPaths[0], StringComparison.OrdinalIgnoreCase);
                            var scope = hive == RegistryHive.CurrentUser ? "Current user" : "All users";
                            var advisory = protectedEntry
                                ? "This entry may support security, accessibility, drivers, or essential hardware. Leave it enabled unless you know its purpose."
                                : hive != RegistryHive.CurrentUser
                                    ? "System-wide entries are shown for context and cannot be changed here."
                                    : isCurrentUserRun ? null : "This RunOnce entry is temporary and is shown for context; this version does not modify it.";
                            items.Add(new StartupItem(
                                id, name, command, $"{hive}\\{path} ({view})", scope,
                                ReadPublisher(command), true, isCurrentUserRun && !protectedEntry, advisory));
                        }
                    }
                    catch (UnauthorizedAccessException ex) { AppLog.Write($"A startup registry location could not be read ({hive}).", ex); }
                    catch (System.Security.SecurityException ex) { AppLog.Write($"A startup registry location could not be read ({hive}).", ex); }
                }
            }
        }

        foreach (var backup in ReadBackups())
        {
            if (items.Any(item => item.Id == backup.Id)) continue;
            items.Add(new StartupItem(backup.Id, backup.ValueName, backup.Command, backup.Location,
                "Current user", ReadPublisher(backup.Command), false, true,
                "This entry is disabled and has a local backup. Restore it to run it again at sign-in."));
        }
        AddFolderEntries(items, Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Current user");
        AddFolderEntries(items, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), "All users");
        return items.OrderBy(item => item.Scope, StringComparer.Ordinal).ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static void AddFolderEntries(ICollection<StartupItem> items, string folder, string scope)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return;
        try
        {
            foreach (var path in Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly).Take(250))
            {
                var extension = Path.GetExtension(path);
                if (!new[] { ".lnk", ".url", ".exe" }.Contains(extension, StringComparer.OrdinalIgnoreCase)) continue;
                var name = Path.GetFileNameWithoutExtension(path);
                var id = Convert.ToBase64String(Encoding.UTF8.GetBytes($"Folder|{scope}|{Path.GetFullPath(path)}"));
                items.Add(new StartupItem(id, name, path, folder, scope, ReadPublisher(path), true, false,
                    "This startup shortcut is shown for context. This version does not modify Startup-folder files."));
            }
        }
        catch (UnauthorizedAccessException ex) { AppLog.Write("A startup folder could not be read.", ex); }
        catch (IOException ex) { AppLog.Write("A startup folder could not be read.", ex); }
    }

    public StartupMutationResult SetEnabled(StartupItem item, bool enabled)
    {
        if (!item.CanChange) return new(false, "This startup entry is read-only or was marked as potentially essential.");
        if (!TryParseId(item.Id, out var hive, out var view, out var keyPath, out var valueName) || hive != RegistryHive.CurrentUser)
            return new(false, "The startup entry could not be safely identified.");
        try
        {
            Directory.CreateDirectory(_backupDirectory);
            var backupPath = GetBackupPath(item.Id);
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, view);
            if (!enabled)
            {
                using var key = baseKey.OpenSubKey(keyPath, writable: true);
                if (key is null) return new(false, "The original startup setting is no longer present.");
                var currentValue = key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                if (currentValue is null || !string.Equals(currentValue.ToString(), item.Command, StringComparison.Ordinal))
                    return new(false, "The startup setting changed since the list was read. Refresh the list and review it again.");
                var originalKind = key.GetValueKind(valueName);
                var backup = new StartupBackup(item.Id, valueName, item.Command, keyPath, view.ToString(),
                    originalKind.ToString(), item.Location);
                WriteBackup(backupPath, backup);
                try
                {
                    key.DeleteValue(valueName, throwOnMissingValue: true);
                    if (key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is not null)
                        throw new IOException("Windows did not confirm that the startup value was removed.");
                }
                catch
                {
                    key.SetValue(valueName, currentValue, originalKind);
                    File.Delete(backupPath);
                    throw;
                }
                return new(true, "The current-user startup value was removed and backed up. No running program was terminated.", backupPath);
            }

            if (!File.Exists(backupPath)) return new(false, "No local backup was found for this startup entry.");
            var saved = JsonSerializer.Deserialize<StartupBackup>(File.ReadAllText(backupPath));
            if (saved is null || saved.Id != item.Id || saved.ValueName != valueName || saved.KeyPath != keyPath ||
                !Enum.TryParse<RegistryView>(saved.View, out var savedView) || savedView != view || !RunKeyPaths.Contains(saved.KeyPath, StringComparer.OrdinalIgnoreCase))
                return new(false, "The startup backup could not be verified.");
            using var restoreKey = baseKey.CreateSubKey(saved.KeyPath, writable: true);
            if (restoreKey is null) return new(false, "Windows did not allow the current-user startup key to be opened.");
            var existing = restoreKey.GetValue(saved.ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            if (existing is not null && !string.Equals(existing.ToString(), saved.Command, StringComparison.Ordinal))
                return new(false, "A different startup value now uses this name. The backup was kept to avoid overwriting it.");
            var kind = Enum.TryParse<RegistryValueKind>(saved.ValueKind, out var parsed) ? parsed : RegistryValueKind.String;
            restoreKey.SetValue(saved.ValueName, saved.Command, kind);
            if (!string.Equals(restoreKey.GetValue(saved.ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames)?.ToString(), saved.Command, StringComparison.Ordinal))
                return new(false, "Windows did not verify the restored startup value.");
            File.Delete(backupPath);
            return new(true, "The original current-user startup value was restored and verified.");
        }
        catch (UnauthorizedAccessException ex)
        {
            AppLog.Write("Startup change was denied by Windows.", ex);
            return new(false, "Windows denied access. The startup setting was not changed.");
        }
        catch (Exception ex)
        {
            AppLog.Write("Startup change did not complete.", ex);
            return new(false, $"The startup change could not be completed: {ex.Message}");
        }
    }

    private IEnumerable<StartupBackup> ReadBackups()
    {
        if (!Directory.Exists(_backupDirectory)) yield break;
        foreach (var path in Directory.EnumerateFiles(_backupDirectory, "*.json", SearchOption.TopDirectoryOnly).Take(200))
        {
            StartupBackup? backup = null;
            try { backup = JsonSerializer.Deserialize<StartupBackup>(File.ReadAllText(path)); }
            catch (Exception ex) { AppLog.Write("A disabled-startup backup could not be read.", ex); }
            if (backup is not null && TryParseId(backup.Id, out var hive, out var view, out var keyPath, out var valueName) &&
                hive == RegistryHive.CurrentUser && RunKeyPaths.Contains(keyPath, StringComparer.OrdinalIgnoreCase) &&
                backup.KeyPath == keyPath && backup.ValueName == valueName && Enum.TryParse<RegistryView>(backup.View, out var backupView) && backupView == view)
                yield return backup;
        }
    }

    private static void WriteBackup(string path, StartupBackup backup)
    {
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(backup));
        File.Move(temporary, path, true);
    }

    private string GetBackupPath(string id) => Path.Combine(_backupDirectory, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id))) + ".json");

    private static string MakeId(RegistryHive hive, RegistryView view, string path, string name) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{hive}|{view}|{path}|{name}"));

    private static bool TryParseId(string id, out RegistryHive hive, out RegistryView view, out string path, out string name)
    {
        hive = RegistryHive.LocalMachine;
        view = RegistryView.Default;
        path = string.Empty;
        name = string.Empty;
        try
        {
            var fields = Encoding.UTF8.GetString(Convert.FromBase64String(id)).Split(new[] { '|' }, 4);
            return fields.Length == 4 && Enum.TryParse(fields[0], out hive) && Enum.TryParse(fields[1], out view) &&
                   RunKeyPaths.Contains(path = fields[2], StringComparer.OrdinalIgnoreCase) &&
                   (name = fields[3]).Length > 0;
        }
        catch { return false; }
    }

    private static bool IsSensitive(string name, string command)
    {
        var value = (name + " " + command).ToLowerInvariant();
        return new[] { "defender", "windows security", "accessibility", "ease of access", "driver", "touchpad", "antivirus", "firewall", "uac", "backup" }
            .Any(marker => value.Contains(marker, StringComparison.Ordinal));
    }

    private static string? ReadPublisher(string command)
    {
        var executable = FindExecutable(command);
        if (executable is null || !File.Exists(executable)) return null;
        try { return FileVersionInfo.GetVersionInfo(executable).CompanyName; }
        catch { return null; }
    }

    private static string? FindExecutable(string command)
    {
        var expanded = Environment.ExpandEnvironmentVariables(command.Trim());
        if (expanded.StartsWith('"'))
        {
            var end = expanded.IndexOf('"', 1);
            return end > 1 ? expanded[1..end] : null;
        }
        var extension = expanded.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return extension >= 0 ? expanded[..(extension + 4)].Trim() : null;
    }

    private sealed record StartupBackup(string Id, string ValueName, string Command, string KeyPath, string View, string ValueKind, string Location);
}
