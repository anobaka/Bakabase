using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Infrastructures.Components.App;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Workflow.Artifacts;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Service.Components.FileProcessing;

public sealed record FileArtifactPublicationResult(ArtifactRecord? Artifact, string? WaitReason = null, string? Message = null);
public sealed record FileArtifactPublicationMetadata(string SourceIntent, string SourceArtifactId, string SourceOperationId,
    string? SourceMetadataJson, FileDispositionProof? FileDisposition = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    ArtifactDeliveryLayoutProof? DeliveryLayout = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    ArtifactDirectoryProvenance? DirectoryProvenance = null);

/// <summary>Publishes exact immutable manifests by verified copy. Shared source artifacts are never moved.</summary>
public sealed class FileArtifactPublisher
{
    private readonly IArtifactOperationStore _store;
    private readonly IUserStoragePolicy _storage;
    private readonly Func<string> _appData;
    private readonly IServiceScopeFactory _scopes;
    private readonly Func<Stream, Stream, CancellationToken, Task>? _copy;
    private readonly IStorageWriteGuard? _writeGuard;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(ArtifactManifestFiles.PathComparer);
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(2);

    public FileArtifactPublisher(IArtifactOperationStore store, IUserStoragePolicy storage, AppService app, IServiceScopeFactory scopes,
        IStorageWriteGuard? writeGuard = null)
        : this(store, storage, () => app.AppDataDirectory, scopes, writeGuard: writeGuard) { }
    internal FileArtifactPublisher(IArtifactOperationStore store, IUserStoragePolicy storage, Func<string> appData, IServiceScopeFactory scopes,
        Func<Stream, Stream, CancellationToken, Task>? copy = null, IStorageWriteGuard? writeGuard = null)
    { _store = store; _storage = storage; _appData = appData; _scopes = scopes; _copy = copy; _writeGuard = writeGuard; }

    public static string Intent(string sourceIntent, string targetDirectory, string? forceKey = null,
        FileProcessingWorkspaceResolver? workspaces = null, ArtifactDeliveryPolicy? deliveryPolicy = null,
        string? currentStageDirectoryBindingId = null)
    {
        FileArtifactDeliveryLayout.Validate(deliveryPolicy);
        if (currentStageDirectoryBindingId != null && !Guid.TryParseExact(currentStageDirectoryBindingId, "N", out _))
            throw new ArgumentException("STAGE_DIRECTORY_BINDING_INVALID");
        var legacy = "publish-artifacts-v1\n" + sourceIntent + "\n" +
            (workspaces?.IdentityPath(Path.GetFullPath(targetDirectory)) ?? Path.GetFullPath(targetDirectory)) + "\n" + forceKey;
        if (currentStageDirectoryBindingId != null) legacy += "\ncurrent-stage-directory-v1\n" + currentStageDirectoryBindingId;
        return ArtifactManifestFiles.Hash(deliveryPolicy == null ? legacy :
            legacy + "\ndelivery-layout-v1\n" + JsonSerializer.Serialize(deliveryPolicy, Json));
    }

    private async Task CopyAsync(Stream source, Stream destination, string path, CancellationToken ct)
    {
        if (_writeGuard == null)
        {
            if (_copy != null) await _copy(source, destination, ct);
            else await source.CopyToAsync(destination, ct);
        }
        else if (_copy == null) await _writeGuard.CopyAsync(source, destination, path, StorageWriteScope.Current, ct);
        else await _writeGuard.RunAsync([path], StorageWriteScope.Current, async token =>
        { await _copy(source, destination, token); return true; }, ct);
    }

    private string StableIntent(string sourceIntent, string directory, string? forceKey = null, ArtifactDeliveryPolicy? deliveryPolicy = null,
        string? currentStageDirectoryBindingId = null) =>
        Intent(sourceIntent, directory, forceKey, new FileProcessingWorkspaceResolver(_appData(), _storage), deliveryPolicy, currentStageDirectoryBindingId);

    public async Task<ArtifactRecord?> ResolveAsync(string sourceIntent, string targetDirectory, CancellationToken ct = default,
        ArtifactDeliveryPolicy? deliveryPolicy = null, string? currentStageDirectoryBindingId = null)
    {
        ValidateTarget(targetDirectory);
        var found = await _store.ResolveAsync(StableIntent(sourceIntent, targetDirectory, deliveryPolicy: deliveryPolicy,
            currentStageDirectoryBindingId: currentStageDirectoryBindingId), ct);
        return found != null && MatchesPolicy(found, deliveryPolicy) ? found : null;
    }

    private static bool MatchesPolicy(ArtifactRecord artifact, ArtifactDeliveryPolicy? policy)
    {
        if (policy == null) return true; // Historical immutable publication identity remains reusable.
        try
        {
            var proof = JsonSerializer.Deserialize<FileArtifactPublicationMetadata>(artifact.MetadataJson ?? "{}", Json)?.DeliveryLayout;
            return proof?.Policy == policy && proof.OutputFingerprint == artifact.Fingerprint &&
                   FileArtifactDeliveryLayout.ValidPublication(proof, artifact.Manifest);
        }
        catch (JsonException) { return false; }
    }

    public void ValidateTarget(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Path.IsPathFullyQualified(directory))
            throw new ArgumentException("Choose an absolute user delivery directory.");
        _storage.EnsurePathAllowed(directory);
        var appData = Path.TrimEndingDirectorySeparator(Path.GetFullPath(_appData()));
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (full.Equals(appData, comparison) || full.StartsWith(appData + Path.DirectorySeparatorChar, comparison))
            throw new ArgumentException("Delivery must use a user directory outside application data.");
        ArtifactManifestFiles.RejectLinks(full);
    }

    public async Task<FileArtifactPublicationResult> PublishAsync(string sourceIntent, ArtifactRecord input,
        string targetDirectory, string consumerKey, long? workflowRunId = null, CancellationToken ct = default,
        string? forceKey = null, ArtifactDeliveryPolicy? deliveryPolicy = null, string? currentStageDirectoryBindingId = null)
    {
        using var writeScope = workflowRunId is { } runtimeOwner
            ? StorageWriteScope.Enter(new("workflow", runtimeOwner.ToString(System.Globalization.CultureInfo.InvariantCulture))) : null;
        ValidateTarget(targetDirectory);
        targetDirectory = Path.GetFullPath(targetDirectory);
        var intent = StableIntent(sourceIntent, targetDirectory, forceKey, deliveryPolicy, currentStageDirectoryBindingId);
        var gate = Gates.GetOrAdd(targetDirectory, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        ArtifactOperationLease? lease = null;
        var attached = false;
        string? failure = null;
        Task<FileArtifactPublicationResult> Wait(string reason, string message)
        {
            failure = message;
            return Task.FromResult(new FileArtifactPublicationResult(null, reason, message));
        }
        try
        {
            var existing = await _store.ResolveAsync(intent, ct);
            if (existing != null && deliveryPolicy == null) return new(existing);
            var layout = FileArtifactDeliveryLayout.Preview(input, deliveryPolicy);
            if (existing != null)
            {
                if (!MatchesPolicy(existing, deliveryPolicy))
                    return await Wait("PublicationConflict", "The saved delivery layout differs from the requested policy.");
                if (deliveryPolicy != null && JsonSerializer.Deserialize<FileArtifactPublicationMetadata>(existing.MetadataJson!, Json)?.DeliveryLayout
                        ?.ProjectionFingerprint != layout.ProjectionFingerprint)
                    return await Wait("PublicationConflict", "The saved delivery projection differs from the current source evidence.");
                return new(existing);
            }
            if (currentStageDirectoryBindingId == null && deliveryPolicy != null && Directory.Exists(targetDirectory) && OtherLayoutPath(input, targetDirectory, layout) is { } previous)
                return new(null, "PublicationConflict", $"Another delivery layout already uses '{previous}'. Choose a new delivery directory or review the existing files first.");
            var validation = await ArtifactManifestFiles.ValidateAsync(input.RootDirectory, input.Manifest, ct);
            if (!validation.Available) return new(null,
                validation.State == ArtifactLocationState.StorageUnavailable ? "StorageUnavailable" : "PublicationInputMissing",
                validation.Message);
            var sourcePaths = ArtifactManifestFiles.ResolveFiles(input.RootDirectory, input.Manifest);
            var outputManifest = layout.OutputManifest;
            var outputEntries = outputManifest.Entries.ToDictionary(e => e.RelativePath, StringComparer.Ordinal);
            var destinationPaths = layout.FileMappings.Select(m => FileProcessingFiles.Within(
                Path.Combine(targetDirectory, m.TargetRelativePath), targetDirectory)).ToArray();
            var sourceSet = sourcePaths.ToHashSet(ArtifactManifestFiles.PathComparer);
            if (currentStageDirectoryBindingId == null && destinationPaths.Any(sourceSet.Contains))
                return new(null, "PublicationConflict", "Delivery must copy to a different directory from the shared source artifact.");
            var key = ArtifactManifestFiles.Hash(intent + "\n" + input.Fingerprint +
                (deliveryPolicy == null ? "" : "\n" + layout.ProjectionFingerprint));
            var claim = await _store.ClaimAsync(new(key, intent, "files.publish", input.Fingerprint), consumerKey, LeaseDuration, ct);
            if (claim.Artifact != null) return new(claim.Artifact);
            if (claim.Status == ArtifactClaimStatus.StorageUnavailable)
                return new(null, "StorageUnavailable", "The existing delivery directory is unavailable. Reconnect its storage to reuse the delivered files.");
            if (claim.Lease == null) return new(null, "WaitingShared", "Another publication owns this target. Resume to check its result.");
            lease = claim.Lease;
            using var work = CancellationTokenSource.CreateLinkedTokenSource(ct);
            using var stopHeartbeat = CancellationTokenSource.CreateLinkedTokenSource(ct);
            // Initial attachment can wait behind maintenance too. Renew the operation as soon
            // as it is claimed, then renew the source consumer after that attachment succeeds.
            var heartbeat = Heartbeat(lease, input.OperationId, consumerKey, workflowRunId,
                () => Volatile.Read(ref attached), work, stopHeartbeat.Token);
            try
            {
                attached = workflowRunId is { } run
                    ? await _store.PinConsumerAsync(input.OperationId, consumerKey, "workflow", run.ToString(), work.Token)
                    : await _store.AttachConsumerAsync(input.OperationId, consumerKey, LeaseDuration, work.Token);
                if (!attached) return await Wait("WaitingShared", "The source artifact is being cleaned. Resume after cleanup finishes.");
                Directory.CreateDirectory(targetDirectory);
                var staging = Path.Combine(targetDirectory, ".bakabase-publish-" + key);
                ArtifactManifestFiles.RejectLinks(staging);
                // This reserved staging name cannot also be an input's public file path.
                EnsureOutsideStaging(staging, destinationPaths.Concat(
                    ArtifactManifestFiles.ResolveDirectories(targetDirectory, outputManifest)));
                ArtifactManifestFiles.CreateDirectories(targetDirectory, outputManifest);
                var ownerFile = Path.Combine(staging, ".owner");
                var ownership = key + ":" + input.Fingerprint;
                if (Directory.Exists(staging) && !File.Exists(ownerFile) && Directory.EnumerateFileSystemEntries(staging).Any())
                    return await Wait("PublicationConflict", "An unrecognized delivery staging directory already exists. It was retained.");
                Directory.CreateDirectory(staging);
                ArtifactManifestFiles.RejectLinks(ownerFile);
                if (File.Exists(ownerFile))
                {
                    if (!await OwnsStage(ownerFile, ownership, work.Token))
                        return await Wait("PublicationConflict", "The delivery staging directory belongs to another operation.");
                }
                else
                {
                    await using var owner = new FileStream(ownerFile, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    var bytes = System.Text.Encoding.UTF8.GetBytes(ownership);
                    await owner.WriteAsync(bytes, work.Token); await owner.FlushAsync(work.Token);
                }
                var stagingFiles = Path.Combine(staging, "files");
                Directory.CreateDirectory(stagingFiles);
                for (var index = 0; index < destinationPaths.Length; index++)
                {
                    work.Token.ThrowIfCancellationRequested();
                    var entry = outputEntries[layout.FileMappings[index].TargetRelativePath];
                    var destination = destinationPaths[index];
                    ArtifactManifestFiles.RejectLinks(destination);
                    if (File.Exists(destination))
                    {
                        if (!await Matches(destination, entry, work.Token))
                            return await Wait("PublicationConflict", "A different file already exists in the delivery directory. Choose another directory; existing files are never overwritten.");
                        // Either a prior copy committed before its receipt, or an explicit current
                        // stage already contains this exact registered input. Both require the
                        // complete manifest hash; never replace different bytes or adopt siblings.
                        continue;
                    }
                    if (Directory.Exists(destination))
                        return await Wait("PublicationConflict", "A delivery file conflicts with an existing directory.");
                    var pending = FileProcessingFiles.Within(Path.Combine(stagingFiles, entry.RelativePath), stagingFiles);
                    ArtifactManifestFiles.RejectLinks(pending);
                    Directory.CreateDirectory(Path.GetDirectoryName(pending)!);
                    if (File.Exists(pending) && !await Matches(pending, entry, work.Token)) File.Delete(pending);
                    if (!File.Exists(pending))
                    {
                        ArtifactManifestFiles.RejectLinks(sourcePaths[index]);
                        if (_writeGuard != null) await _writeGuard.CheckAsync(pending, StorageWriteScope.Current, work.Token);
                        await using var source = new FileStream(sourcePaths[index], FileMode.Open, FileAccess.Read, FileShare.Read);
                        await using var output = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                        await CopyAsync(source, output, pending, work.Token);
                        await output.FlushAsync(work.Token);
                    }
                    if (!await Matches(pending, entry, work.Token))
                        throw new InvalidDataException("The source changed during delivery; no changed file was published.");
                    ArtifactManifestFiles.RejectLinks(destination);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.Move(pending, destination, overwrite: false);
                    if (!await Matches(destination, entry, work.Token))
                        throw new InvalidDataException("A delivered file changed before it could be verified.");
                }
                var verified = await ArtifactManifestFiles.ValidateAsync(targetDirectory, outputManifest, work.Token);
                if (verified.State == ArtifactLocationState.StorageUnavailable)
                    return new(null, "StorageUnavailable", verified.Message);
                if (!verified.Available) throw new InvalidDataException(verified.Message);
                var lineage = new FileLineage(layout.FileMappings.Select(m => new FileLineageEntry(
                    m.SourceRelativePath, [m.TargetRelativePath])).ToArray(), input.Manifest, outputManifest);
                var layoutProof = deliveryPolicy == null ? null : new ArtifactDeliveryLayoutProof(1, input.Id, input.Fingerprint,
                    deliveryPolicy, outputManifest.Fingerprint, layout.ProjectionFingerprint, layout.FileMappings, layout.DirectoryMappings);
                var published = await _store.CompleteAsync(lease, new(targetDirectory, destinationPaths, "publishedFiles", false,
                    [input.Id], JsonSerializer.Serialize(new FileArtifactPublicationMetadata(sourceIntent, input.Id, input.OperationId,
                        input.MetadataJson, FileDispositionProofs.Create(input, outputManifest, lineage), layoutProof,
                        FileArtifactDeliveryLayout.ProjectProvenance(input, layout)), Json),
                    ExpectedManifestFingerprint: outputManifest.Fingerprint,
                    Directories: ArtifactManifestFiles.ResolveDirectories(targetDirectory, outputManifest)), work.Token);
                try
                {
                    if (await OwnsStage(ownerFile, ownership, CancellationToken.None)) File.Delete(ownerFile);
                    DeleteEmptyTree(staging);
                }
                catch (Exception cleanupError) when (cleanupError is IOException or UnauthorizedAccessException)
                { /* Delivery is committed; incidental staging cleanup cannot invalidate it. */ }
                return new(published);
            }
            finally
            {
                stopHeartbeat.Cancel();
                try { await heartbeat; } catch (OperationCanceledException) { }
            }
        }
        catch (InvalidDataException error)
        { return await Wait("PublicationConflict", error.Message); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Unavailable storage does not invalidate the attempt or its copied files. The
            // same owner (or a later expired-lease takeover) resumes this exact generation.
            // Preserve a typed storage cause before projecting the diagnostic into the
            // workflow. The generic publication prefix would otherwise hide its code.
            // Arbitrary I/O message text is not evidence of a full disk or low reserve.
            return new(null, "StorageUnavailable",
                LocalStorageFailures.Code(error) ?? "PUBLICATION_IO_ERROR: " + error.Message);
        }
        finally
        {
            // The inner finally has stopped the heartbeat before this state transition, so
            // expected conflicts cannot race renewal and turn a suspension into a lost lease.
            if (lease != null && failure != null) await _store.FailAsync(lease, failure, ct: CancellationToken.None);
            if (attached) await _store.ReleaseConsumerAsync(input.OperationId, consumerKey, CancellationToken.None);
            gate.Release();
        }
    }

    // Do not silently create a second complete tree next to a prior projection. Check
    // only exact manifest-derived alternatives (never search or delete unrelated files).
    private static string? OtherLayoutPath(ArtifactRecord input, string target, ArtifactDeliveryLayoutPreview current)
    {
        var files = current.FileMappings.Select(m => m.TargetRelativePath).ToHashSet(ArtifactManifestFiles.PathComparer);
        var directories = current.DirectoryMappings.Select(m => m.TargetRelativePath).Where(p => p.Length > 0)
            .ToHashSet(ArtifactManifestFiles.PathComparer);
        foreach (var path in files.Concat(directories.ToArray()))
        {
            var parent = Path.GetDirectoryName(path);
            while (!string.IsNullOrEmpty(parent)) { directories.Add(parent.Replace('\\', '/')); parent = Path.GetDirectoryName(parent); }
        }
        var oldFiles = new HashSet<string>(ArtifactManifestFiles.PathComparer);
        var oldDirectories = new HashSet<string>(ArtifactManifestFiles.PathComparer);
        foreach (var remove in new[] {false, true})
        foreach (var collapse in new[] {false, true})
        {
            var alternate = new ArtifactDeliveryPolicy {RemoveGeneratedDirectories = remove, CollapseSingleRootDirectory = collapse};
            if (alternate == current.Policy) continue;
            ArtifactDeliveryLayoutPreview preview;
            try { preview = FileArtifactDeliveryLayout.Preview(input, alternate); }
            catch (InvalidDataException) { continue; } // This colliding projection could not have been published.
            oldFiles.UnionWith(preview.FileMappings.Select(m => m.TargetRelativePath));
            oldDirectories.UnionWith(preview.DirectoryMappings.Select(m => m.TargetRelativePath).Where(p => p.Length > 0));
        }
        foreach (var relative in oldFiles.Except(files, ArtifactManifestFiles.PathComparer))
        {
            var path = FileProcessingFiles.Within(Path.Combine(target, relative), target);
            if (File.Exists(path)) return relative;
        }
        foreach (var relative in oldDirectories.Except(directories, ArtifactManifestFiles.PathComparer))
        {
            var path = FileProcessingFiles.Within(Path.Combine(target, relative), target);
            if (Directory.Exists(path)) return relative;
        }
        return null;
    }

    private async Task Heartbeat(ArtifactOperationLease lease, string sourceOperation, string consumer, long? run,
        Func<bool> isAttached, CancellationTokenSource work, CancellationToken ct)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
            while (await timer.WaitForNextTickAsync(ct))
            {
                await using var scope = _scopes.CreateAsyncScope();
                var store = scope.ServiceProvider.GetRequiredService<IArtifactOperationStore>();
                if (!await store.RenewAsync(lease, LeaseDuration, ct) || run == null && isAttached() &&
                    !await store.RenewExistingConsumerAsync(sourceOperation, consumer, LeaseDuration, ct))
                    throw new InvalidOperationException("The publication lease was lost.");
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch { work.Cancel(); throw; }
    }
    internal static void EnsureOutsideStaging(string staging, System.Collections.Generic.IEnumerable<string> paths)
    {
        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (paths.Any(path => path.Equals(staging, comparison) ||
                path.StartsWith(staging + Path.DirectorySeparatorChar, comparison)))
            throw new InvalidDataException("An input path conflicts with the delivery staging directory.");
    }

    private static async Task<bool> OwnsStage(string path, string expected, CancellationToken ct)
    {
        ArtifactManifestFiles.RejectLinks(path);
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var bytes = System.Text.Encoding.UTF8.GetBytes(expected);
        if (file.Length != bytes.Length) return false;
        var actual = new byte[bytes.Length];
        await file.ReadExactlyAsync(actual, ct);
        return file.Length == bytes.Length && actual.SequenceEqual(bytes);
    }
    private static async Task<bool> Matches(string path, ArtifactManifestEntry entry, CancellationToken ct)
    {
        ArtifactManifestFiles.RejectLinks(path);
        if (!File.Exists(path)) return false;
        var manifest = await ArtifactManifestFiles.CaptureAsync(Path.GetDirectoryName(path)!, new[] {path}, ct);
        return manifest.Entries[0].Length == entry.Length && manifest.Entries[0].Sha256.Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase);
    }
    internal static void DeleteEmptyTree(string root)
    {
        if (!Directory.Exists(root)) return;
        ArtifactManifestFiles.RejectLinks(root);
        foreach (var directory in Directory.EnumerateDirectories(root)) DeleteEmptyTree(directory);
        if (!Directory.EnumerateFileSystemEntries(root).Any()) Directory.Delete(root, recursive: false);
    }
}
