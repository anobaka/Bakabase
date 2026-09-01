using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.InsideWorld.Business.Services;

public class FileRenameEntryService(BakabaseDbContext db) : IFileRenameEntryService
{
    public async Task<List<FileRenameEntry>> GetByRunId(int runId) =>
        await db.FileRenameEntries.AsNoTracking()
            .Where(e => e.RunId == runId)
            .OrderBy(e => e.Seq)
            .ToListAsync();

    public async Task<FileRenameEntry> AddToPlan(int runId, string path, string from, string to,
        FileRenameStatus status, string? error = null)
    {
        // Seq is per-run and runs of one definition are serialized by their BTask conflict key,
        // so max+1 inside this scoped context cannot race itself.
        var maxSeq = await db.FileRenameEntries
            .Where(e => e.RunId == runId)
            .MaxAsync(e => (int?) e.Seq) ?? 0;

        var entry = new FileRenameEntry
        {
            RunId = runId,
            Seq = maxSeq + 1,
            Path = path,
            From = from,
            To = to,
            Status = status,
            Error = error,
            CreatedAt = DateTime.Now
        };
        db.FileRenameEntries.Add(entry);
        await db.SaveChangesAsync();
        return entry;
    }

    public async Task<FileRenameEntry> SetExcluded(int id, bool excluded)
    {
        var entry = await db.FileRenameEntries.FirstOrDefaultAsync(e => e.Id == id)
                    ?? throw new InvalidOperationException($"Rename entry #{id} does not exist.");

        var expected = excluded ? FileRenameStatus.Pending : FileRenameStatus.Excluded;
        if (entry.Status != expected)
        {
            throw new InvalidOperationException(
                $"Rename entry #{id} is {entry.Status} and cannot be {(excluded ? "excluded" : "re-included")}.");
        }

        entry.Status = excluded ? FileRenameStatus.Excluded : FileRenameStatus.Pending;
        await db.SaveChangesAsync();
        return entry;
    }

    public async Task<List<FileRenameEntry>> ApplyRun(int runId)
    {
        var rows = await db.FileRenameEntries
            .Where(e => e.RunId == runId && e.Status == FileRenameStatus.Pending)
            .ToListAsync();

        // Deepest first: children are renamed while their ancestors' recorded paths are still
        // real; a parent directory only moves after everything beneath it is done.
        foreach (var entry in rows.OrderByDescending(e => Depth(e.Path)).ThenByDescending(e => e.Seq))
        {
            var parent = System.IO.Path.GetDirectoryName(entry.Path);
            var target = parent == null ? null : System.IO.Path.Combine(parent, entry.To);
            try
            {
                var isDirectory = Directory.Exists(entry.Path);
                if (target == null)
                {
                    Fail(entry, "The entry has no parent directory.");
                }
                else if (!isDirectory && !File.Exists(entry.Path))
                {
                    Fail(entry, "The source no longer exists — the disk changed after the preview.");
                }
                else if (!SamePath(target, entry.Path) && (File.Exists(target) || Directory.Exists(target)))
                {
                    Fail(entry, "The target name is now taken — the disk changed after the preview.");
                }
                else
                {
                    if (isDirectory)
                    {
                        Directory.Move(entry.Path, target!);
                    }
                    else
                    {
                        File.Move(entry.Path, target!);
                    }

                    entry.Status = FileRenameStatus.Applied;
                    entry.AppliedAt = DateTime.Now;
                    entry.Error = null;
                }
            }
            catch (Exception ex)
            {
                Fail(entry, ex.Message);
            }

            // Persisted per row on purpose: the row IS the crash-consistency record.
            await db.SaveChangesAsync();
        }

        return await GetByRunId(runId);
    }

    public async Task<List<FileRenameEntry>> UndoRun(int runId)
    {
        var rows = await db.FileRenameEntries
            .Where(e => e.RunId == runId && e.Status == FileRenameStatus.Applied)
            .ToListAsync();

        // Reverse of apply: ancestors regain their old names first, which restores the prefix
        // their children's recorded paths rely on.
        foreach (var entry in rows.OrderBy(e => Depth(e.Path)).ThenBy(e => e.Seq))
        {
            var parent = System.IO.Path.GetDirectoryName(entry.Path)!;
            var current = System.IO.Path.Combine(parent, entry.To);
            try
            {
                var isDirectory = Directory.Exists(current);
                if (!isDirectory && !File.Exists(current))
                {
                    entry.Error = "Cannot undo: the renamed entry no longer exists at its new name.";
                }
                else if (!SamePath(entry.Path, current) &&
                         (File.Exists(entry.Path) || Directory.Exists(entry.Path)))
                {
                    entry.Error = "Cannot undo: the original name is taken again.";
                }
                else
                {
                    if (isDirectory)
                    {
                        Directory.Move(current, entry.Path);
                    }
                    else
                    {
                        File.Move(current, entry.Path);
                    }

                    entry.Status = FileRenameStatus.Undone;
                    entry.Error = null;
                }
            }
            catch (Exception ex)
            {
                entry.Error = $"Cannot undo: {ex.Message}";
            }

            await db.SaveChangesAsync();
        }

        return await GetByRunId(runId);
    }

    public async Task DeleteByRunIds(IReadOnlyCollection<int> runIds)
    {
        if (runIds.Count > 0)
        {
            await db.FileRenameEntries.Where(e => runIds.Contains(e.RunId)).ExecuteDeleteAsync();
        }
    }

    private static void Fail(FileRenameEntry entry, string error)
    {
        entry.Status = FileRenameStatus.Failed;
        entry.Error = error;
    }

    private static int Depth(string path) => path.Count(c => c is '/' or '\\');

    private static bool SamePath(string a, string b) =>
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    public async Task<bool> IsTargetPlanned(int runId, string targetFullPath)
    {
        var rows = await db.FileRenameEntries.AsNoTracking()
            .Where(e => e.RunId == runId &&
                        e.Status != FileRenameStatus.Conflict)
            .Select(e => new {e.Path, e.To})
            .ToListAsync();

        // The comparison needs Path.GetDirectoryName + case-insensitivity, which SQLite can't
        // express reliably — a run's plan is small enough to compare in memory.
        return rows.Any(r =>
        {
            var parent = System.IO.Path.GetDirectoryName(r.Path);
            if (parent == null || r.To.Length == 0)
            {
                return false;
            }

            var planned = System.IO.Path.Combine(parent, r.To);
            return string.Equals(planned, targetFullPath, StringComparison.OrdinalIgnoreCase);
        });
    }
}
