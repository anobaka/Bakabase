using System;
using System.Collections.Generic;
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
