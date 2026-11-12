using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Workflow.Artifacts;

namespace Bakabase.Service.Components.FileProcessing;

public sealed partial class FileProcessingWorkspaceResolver
{
    // Written only while holding the resolver gate after the caller has acquired a new
    // operation attempt. This is an allocation intent, never proof that payload files exist.
    // The normal exact locator replaces it as the authority once the directory is created.
    private sealed record Allocation(int Version, string Kind, string Key, int Attempt, string StorageRoot,
        ArtifactDirectoryScope? Scope = null);

    internal async Task<string> ResolveOperationAsync(string key, int attempt, string inputRoot,
        string? configuredRoot, ArtifactDirectoryScope? scope, bool newAttempt, ArtifactStoragePolicy? policy,
        string? title, CancellationToken ct)
    {
        Allocation? allocation;
        await Gate.WaitAsync(ct);
        try
        {
            // Do not even validate today's defaults before the attempt's own locator.
            // A settings change cannot move an existing operation or make it start over.
            if (await ReadLocationAsync("operations", key, attempt, ct) != null) allocation = null;
            else
            {
                allocation = await ReadAllocationAsync("operations", key, attempt, ct);
                if (allocation == null && newAttempt)
                    allocation = await SaveAllocationAsync("operations", key, attempt,
                        scope?.Directory ?? (string.IsNullOrWhiteSpace(configuredRoot) ? DefaultRoot(inputRoot) : configuredRoot), ct, scope);
            }
        }
        finally { Gate.Release(); }

        if (allocation != null)
        {
            configuredRoot = Relocate(allocation.StorageRoot);
            scope = allocation.Scope;
        }
        if (await ReadLocationAsync("operations", key, attempt, ct) == null && scope != null)
            await EnsureScopedDirectoryAsync(scope, ct);
        return await ResolveAsync("operations", key, attempt, inputRoot, configuredRoot, ct, policy, title);
    }

    private string AllocationFile(string kind, string key, int attempt) =>
        Path.Combine(MetadataRoot, "allocations", $"{kind}-{key}-{attempt}.json");

    private async Task<Allocation?> ReadAllocationAsync(string kind, string key, int attempt, CancellationToken ct)
    {
        var file = AllocationFile(kind, key, attempt);
        ArtifactManifestFiles.RejectLinks(file);
        if (!File.Exists(file)) return null;
        if (new FileInfo(file).Length > 16384) throw new InvalidDataException("The workspace allocation is too large.");
        var allocation = JsonSerializer.Deserialize<Allocation>(await File.ReadAllTextAsync(file, ct), Json);
        if (allocation is not {Version: 1} || allocation.Kind != kind || allocation.Key != key ||
            allocation.Attempt != attempt || string.IsNullOrWhiteSpace(allocation.StorageRoot) ||
            !Path.IsPathFullyQualified(allocation.StorageRoot))
            throw new InvalidDataException("The workspace allocation belongs to another operation.");
        if (allocation.Scope != null)
        {
            ValidateDirectoryScope(allocation.Scope);
            if (!ArtifactManifestFiles.PathComparer.Equals(Path.GetFullPath(allocation.Scope.Directory), allocation.StorageRoot))
                throw new InvalidDataException("The workspace allocation has a different task directory.");
        }
        return allocation;
    }

    private async Task<Allocation> SaveAllocationAsync(string kind, string key, int attempt,
        string root, CancellationToken ct, ArtifactDirectoryScope? scope = null)
    {
        if (!Path.IsPathFullyQualified(root)) throw new ArgumentException("Choose an absolute processing workspace directory.");
        if (scope != null) ValidateDirectoryScope(scope);
        var allocation = new Allocation(1, kind, key, attempt,
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)), scope);
        var file = AllocationFile(kind, key, attempt);
        var directory = Path.GetDirectoryName(file)!;
        ArtifactManifestFiles.RejectLinks(directory);
        Directory.CreateDirectory(directory);
        ArtifactManifestFiles.RejectLinks(file);
        var pending = file + ".pending";
        ArtifactManifestFiles.RejectLinks(pending);
        await File.WriteAllTextAsync(pending, JsonSerializer.Serialize(allocation, Json), ct);
        File.Move(pending, file, false);
        return allocation;
    }
}
