using System.Text.Json;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.View;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Modules.Workflow.Components;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Modules.Workflow.Services;

public sealed class WorkflowOutputQueryService<TDbContext>(TDbContext db, IArtifactOperationStore artifacts) where TDbContext : DbContext
{
    private async Task<ArtifactRecord?> ReadOwnedManifestAsync(int runId, string artifactId, CancellationToken ct)
    {
        // A guessed artifact id is insufficient. Only an explicitly recorded input/output reference
        // on this run may expose its manifest. No arbitrary filesystem path is accepted/read.
        var outputs = await db.Set<WorkflowNodeExecutionDbModel>().AsNoTracking()
            .Where(n => n.WorkflowRunId == runId).Select(n => new {n.OutputsJson, n.InputsJson}).ToListAsync(ct);
        if (!outputs.SelectMany(n => new[] {n.OutputsJson, n.InputsJson}).Any(json => WorkflowOutputMetadataCapture.Read(json)
                .Any(b => b.Kind == "artifact" && b.ArtifactId == artifactId))) return null;
        // Get is an inspection-only ledger read. ResolveById would hash files and write state.
        ArtifactRecord? artifact;
        try { artifact = await artifacts.GetArtifactAsync(artifactId, ct); }
        catch (Exception ex) when (ex is InvalidDataException or JsonException or ArgumentException) { return null; }
        if (artifact == null) return null;
        var manifest = artifact.Manifest;
        if (manifest?.Entries == null || manifest.Entries.Any(e => e == null || e.Length < 0 || string.IsNullOrWhiteSpace(e.RelativePath) ||
                Path.IsPathRooted(e.RelativePath) || e.RelativePath.Replace('\\', '/').Split('/').Any(p => p is ".." or ".")))
            return null;
        return artifact;
    }

    public async Task<WorkflowArtifactFilesView?> GetArtifactFilesAsync(int runId, string artifactId,
        int pageIndex = 1, int pageSize = 100, CancellationToken ct = default)
    {
        var artifact = await ReadOwnedManifestAsync(runId, artifactId, ct);
        if (artifact == null) return null;
        var manifest = artifact.Manifest;
        pageIndex = Math.Max(1, pageIndex);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var skip = (int)Math.Min(int.MaxValue, ((long)pageIndex - 1) * pageSize);
        return new(artifactId, artifact.State.ToString().ToLowerInvariant(), manifest.Entries.Count,
            manifest.Entries.Sum(e => e.Length), pageIndex, pageSize, manifest.Entries.Skip(skip).Take(pageSize).ToList())
            {Directory = artifact.RootDirectory};
    }

    public async Task<WorkflowArtifactDirectoryView?> GetArtifactDirectoryAsync(int runId, string artifactId,
        string? directory, int pageIndex, int pageSize, CancellationToken ct, bool locateSingleFile = false)
    {
        // Directory is a key within the recorded manifest, never a server filesystem path.
        var parent = directory ?? "";
        if (parent.Length > 8192 || parent.Contains('\\') || parent.Contains('\0') ||
            (parent.Length > 0 && parent.Split('/').Any(s => s is "" or "." or ".."))) return null;
        var artifact = await ReadOwnedManifestAsync(runId, artifactId, ct);
        if (artifact == null) return null;
        if (locateSingleFile && parent.Length == 0 && artifact.Manifest.Entries.Count == 1)
        {
            var only = artifact.Manifest.Entries[0].RelativePath.Replace('\\', '/');
            var lastSlash = only.LastIndexOf('/');
            parent = lastSlash > 0 ? only[..lastSlash] : "";
        }
        var prefix = parent.Length == 0 ? "" : parent + "/";
        var children = new Dictionary<string, WorkflowArtifactDirectoryEntry>(StringComparer.Ordinal);
        long totalBytes = 0;
        foreach (var entry in artifact.Manifest.Entries)
        {
            ct.ThrowIfCancellationRequested();
            totalBytes += entry.Length;
            var path = entry.RelativePath.Replace('\\', '/');
            if (!path.StartsWith(prefix, StringComparison.Ordinal) || path.Length <= prefix.Length) continue;
            var slash = path.IndexOf('/', prefix.Length);
            if (slash < 0)
                children[path] = new(path, false, entry.Length);
            else
            {
                var folder = path[..slash];
                children.TryAdd(folder, new(folder, true, null));
            }
        }
        if (parent.Length > 0 && children.Count == 0) return null;
        pageSize = Math.Clamp(pageSize, 1, 200);
        pageIndex = Math.Max(1, pageIndex);
        var skip = (int)Math.Min(int.MaxValue, ((long)pageIndex - 1) * pageSize);
        var page = children.Values.OrderByDescending(e => e.IsDirectory)
            .ThenBy(e => e.RelativePath, StringComparer.Ordinal).Skip(skip).Take(pageSize).ToArray();
        return new(artifactId, artifact.State.ToString().ToLowerInvariant(), parent, artifact.Manifest.Entries.Count,
            totalBytes, children.Count, pageIndex, pageSize, page);
    }
}
