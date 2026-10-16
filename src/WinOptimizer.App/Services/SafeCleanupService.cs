using System.IO;
using WinOptimizer.Models;

namespace WinOptimizer.Services;

public sealed class SafeCleanupService
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".tmp", ".temp", ".cache" };
    private readonly string _tempRoot;

    public SafeCleanupService() : this(Path.GetTempPath()) { }

    internal SafeCleanupService(string tempRoot) =>
        _tempRoot = Path.GetFullPath(tempRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

    public Task<CleanupPreview> PreviewAsync(CancellationToken cancellationToken = default) => Task.Run(() => Preview(cancellationToken), cancellationToken);

    public CleanupPreview Preview(CancellationToken cancellationToken = default)
    {
        var cutoff = DateTime.UtcNow.AddDays(-30);
        var files = new List<CleanupCandidate>();
        ulong total = 0;
        try
        {
            foreach (var path in Directory.EnumerateFiles(_tempRoot, "*", SearchOption.TopDirectoryOnly).Take(5001))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (files.Count >= 5000) break;
                if (!IsEligible(path, cutoff, out var info)) continue;
                var length = (ulong)Math.Max(0, info.Length);
                files.Add(new CleanupCandidate(info.FullName, length, info.LastWriteTimeUtc));
                total = ulong.MaxValue - total < length ? ulong.MaxValue : total + length;
            }
        }
        catch (UnauthorizedAccessException ex) { AppLog.Write("Temporary-file preview encountered an access error.", ex); }
        catch (IOException ex) { AppLog.Write("Temporary-file preview encountered an I/O error.", ex); }
        return new CleanupPreview(files, total, "Only top-level .tmp, .temp, and .cache files in the current user's Windows temporary folder, last changed at least 30 days ago. Folders and other locations are excluded.", cutoff);
    }

    public Task<CleanupResult> ApplyAsync(CleanupPreview preview, CancellationToken cancellationToken = default) =>
        Task.Run(() => Apply(preview, cancellationToken), cancellationToken);

    private CleanupResult Apply(CleanupPreview preview, CancellationToken cancellationToken)
    {
        var deleted = 0;
        var skipped = 0;
        ulong recovered = 0;
        foreach (var candidate in preview.Candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsEligible(candidate.Path, preview.Cutoff, out var info))
            {
                skipped++;
                continue;
            }
            try
            {
                var size = (ulong)Math.Max(0, info.Length);
                if (size != candidate.SizeBytes || info.LastWriteTimeUtc != candidate.LastWriteTime.ToUniversalTime())
                {
                    skipped++;
                    continue;
                }
                File.Delete(info.FullName);
                if (!File.Exists(info.FullName))
                {
                    deleted++;
                    recovered = ulong.MaxValue - recovered < size ? ulong.MaxValue : recovered + size;
                }
                else skipped++;
            }
            catch (IOException) { skipped++; }
            catch (UnauthorizedAccessException) { skipped++; }
        }
        return new CleanupResult(deleted, recovered, skipped, DateTimeOffset.Now);
    }

    private bool IsEligible(string path, DateTime cutoff, out FileInfo info)
    {
        info = new FileInfo(path);
        try
        {
            var fullPath = Path.GetFullPath(path);
            if (!fullPath.StartsWith(_tempRoot, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(Path.GetDirectoryName(fullPath)?.TrimEnd(Path.DirectorySeparatorChar), _tempRoot.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) ||
                !AllowedExtensions.Contains(info.Extension)) return false;
            if (!info.Exists || info.LastWriteTimeUtc > cutoff) return false;
            return (File.GetAttributes(fullPath) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == 0;
        }
        catch { return false; }
    }
}

