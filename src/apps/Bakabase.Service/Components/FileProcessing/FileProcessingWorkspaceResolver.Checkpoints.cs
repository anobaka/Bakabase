using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Workflow.Artifacts;

namespace Bakabase.Service.Components.FileProcessing;

public sealed partial class FileProcessingWorkspaceResolver
{
    private sealed record CheckpointChoice(string Key, string? UnstartedRoot = null, string? PinnedRoot = null);
    internal sealed record CheckpointInspection(string Directory, string StorageRoot);

    private async Task<CheckpointChoice> ChooseCheckpointAsync(string key, string operationKey, int attempt,
        string? explicitlyConfiguredRoot, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(explicitlyConfiguredRoot)) return new(key);
        var operation = await ReadLocationAsync("operations", operationKey, attempt, ct);
        // Legacy attempts without an operation locator retain their original checkpoint.
        if (operation == null) return new(key);
        var destination = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Relocate(operation.StorageRoot)));
        var previous = await ReadLocationAsync("checkpoints", key, attempt, ct);
        if (previous == null) return new(key, PinnedRoot: destination);
        var oldRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Relocate(previous.StorageRoot)));
        if (ArtifactManifestFiles.PathComparer.Equals(destination, oldRoot)) return new(key, PinnedRoot: destination);
        // Use the operation's pinned root, never today's mutable defaults. Separate node
        // operations for the same plan share one deterministic placement namespace.
        var identity = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? destination.ToUpperInvariant() : destination;
        var placed = ArtifactManifestFiles.Hash("checkpoint-placement-v1\n" + key + "\n" + identity);
        var existing = await ReadLocationAsync("checkpoints", placed, attempt, ct);
        if (existing != null)
        {
            if (!ArtifactManifestFiles.PathComparer.Equals(destination,
                    Path.TrimEndingDirectorySeparator(Path.GetFullPath(Relocate(existing.StorageRoot)))))
                throw new InvalidDataException("The processing checkpoint placement belongs to another storage root.");
            return new(placed, PinnedRoot: destination);
        }
        return new(placed, Relocate(previous.Directory), destination);
    }

    internal async Task<Location?> InspectCompletedCheckpointAsync(string key, string operationKey, int attempt,
        string? configuredRoot, CancellationToken ct)
    {
        var choice = await ChooseCheckpointAsync(key, operationKey, attempt, configuredRoot, ct);
        return choice.UnstartedRoot == null ? await InspectExistingAsync("checkpoints", choice.Key, attempt, ct) : null;
    }

    internal async Task<string?> ReadCheckpointAsync(string key, string operationKey, int attempt,
        string? explicitlyConfiguredRoot, CancellationToken ct)
    {
        var choice = await ChooseCheckpointAsync(key, operationKey, attempt, explicitlyConfiguredRoot, ct);
        // A read never creates or implicitly abandons a checkpoint on the prior storage.
        return await ReadExistingAsync("checkpoints", choice.Key, attempt, ct);
    }

    internal async Task<string> ResolveCheckpointAsync(string key, string operationKey, int attempt,
        ArtifactRecord input, FileProcessingPlan plan, string? explicitlyConfiguredRoot, string? selectedRoot,
        ArtifactStoragePolicy? policy, string? title, CancellationToken ct)
    {
        var choice = await ChooseCheckpointAsync(key, operationKey, attempt, explicitlyConfiguredRoot, ct);
        // Only a positively recorded modern operation allocation may skip unbound legacy
        // discovery. Existing checkpoint locators still win, including offline ones.
        var allocatedOperation = await ReadAllocationAsync("operations", operationKey, attempt, ct);
        Task<string> Resolve() => ResolveAsync("checkpoints", choice.Key, attempt, input.RootDirectory,
            choice.PinnedRoot ?? selectedRoot, ct, policy, title, newAttempt: allocatedOperation != null);
        return choice.UnstartedRoot == null ? await Resolve() :
            await FileProcessingPlanExecutor.WithUnstartedCheckpointAsync(choice.UnstartedRoot, plan, input,
                IdentityPath, Resolve, ct, () => ValidateEmptyCheckpointOwnerAsync(key, attempt, choice.UnstartedRoot, ct));
    }

    /// <summary>Read-only recovery may inspect the untouched predecessor before the new location exists.</summary>
    internal async Task<CheckpointInspection?> InspectCheckpointAsync(string key, string operationKey, int attempt,
        ArtifactRecord input, FileProcessingPlan plan, string? explicitlyConfiguredRoot, CancellationToken ct)
    {
        var choice = await ChooseCheckpointAsync(key, operationKey, attempt, explicitlyConfiguredRoot, ct);
        if (choice.UnstartedRoot != null)
            return await FileProcessingPlanExecutor.WithUnstartedCheckpointAsync(choice.UnstartedRoot, plan, input,
                IdentityPath, () => Task.FromResult(new CheckpointInspection(choice.UnstartedRoot, choice.PinnedRoot!)), ct,
                () => ValidateEmptyCheckpointOwnerAsync(key, attempt, choice.UnstartedRoot, ct));
        var existing = await InspectExistingAsync("checkpoints", choice.Key, attempt, ct);
        return existing == null ? null : new(existing.Directory, existing.StorageRoot);
    }

    /// <summary>Known-size admission may stop before the executor creates its first state file.
    /// Only an empty, installation-owned directory with its durable exact locator proves this case.</summary>
    internal async Task<bool> IsUnstartedCheckpointAsync(string key, string operationKey, int attempt,
        ArtifactRecord input, FileProcessingPlan plan, string? explicitlyConfiguredRoot, bool entirePlan, CancellationToken ct)
    {
        try
        {
            if (entirePlan)
            {
                var operation = await ReadExistingAsync("operations", operationKey, attempt, ct);
                if (operation == null || Directory.EnumerateFileSystemEntries(operation).Any()) return false;
                await ValidateEmptyCheckpointOwnerAsync(operationKey, attempt, operation, ct, "operations");
                return Directory.Exists(operation) && !Directory.EnumerateFileSystemEntries(operation).Any();
            }
            var choice = await ChooseCheckpointAsync(key, operationKey, attempt, explicitlyConfiguredRoot, ct);
            var root = choice.UnstartedRoot ?? await ReadExistingAsync("checkpoints", choice.Key, attempt, ct);
            if (root == null || Directory.EnumerateFileSystemEntries(root).Any()) return false;
            return await FileProcessingPlanExecutor.WithUnstartedCheckpointAsync(root, plan, input, IdentityPath,
                () => Task.FromResult(true), ct, () => ValidateEmptyCheckpointOwnerAsync(
                    choice.UnstartedRoot == null ? choice.Key : key, attempt, root, ct));
        }
        catch (FileProcessingCheckpointPlacementException) { return false; }
    }

    private async Task ValidateEmptyCheckpointOwnerAsync(string key, int attempt, string directory, CancellationToken ct,
        string kind = "checkpoints")
    {
        var location = await ReadLocationAsync(kind, key, attempt, ct);
        if (location == null || !ArtifactManifestFiles.PathComparer.Equals(Relocate(location.Directory), directory))
            throw new FileProcessingCheckpointPlacementException();
        RequireExisting(Relocate(location.StorageRoot)); RequireExisting(directory);
        var ownerFile = Path.Combine(MetadataRoot, "owner.json"); ArtifactManifestFiles.RejectLinks(ownerFile);
        if (!File.Exists(ownerFile) || new FileInfo(ownerFile).Length > 128)
            throw new FileProcessingCheckpointPlacementException();
        string? owner;
        try { owner = JsonSerializer.Deserialize<string>(await File.ReadAllTextAsync(ownerFile, ct), Json); }
        catch (JsonException) { throw new FileProcessingCheckpointPlacementException(); }
        if (owner is not {Length: 32} || owner.Any(c => !Uri.IsHexDigit(c)))
            throw new FileProcessingCheckpointPlacementException();
        var ownedRoot = Path.Combine(Relocate(location.StorageRoot), ".bakabase-work", owner);
        var checkpointRoot = Path.Combine(ownedRoot, kind);
        FileProcessingFiles.Within(directory, checkpointRoot);
        var marker = Path.Combine(ownedRoot, "workspace-owner.json"); ArtifactManifestFiles.RejectLinks(marker);
        if (!File.Exists(marker) || new FileInfo(marker).Length != 32 || await File.ReadAllTextAsync(marker, ct) != owner)
            throw new FileProcessingCheckpointPlacementException();
    }
}
