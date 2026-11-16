using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Workflow.Artifacts;

namespace Bakabase.Service.Components.FileProcessing;

/// <summary>
/// Placement is operational metadata, not part of a processing recipe. Locations stay pinned
/// across configuration changes. Large payloads use selected user storage; AppData holds locators.
/// </summary>
public sealed partial class FileProcessingWorkspaceResolver(string appData, IUserStoragePolicy? storage = null)
{
    internal const int MaximumRelocationMappings = 1024;
    internal const int MaximumRelocationBytes = 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private string MetadataRoot => AppDataStorage.ArtifactWorkspaces.GetPath(appData);
    internal string StageCommitMetadataDirectory => Path.Combine(MetadataRoot, "stage-commits");
    public sealed record Location(int Version, string Kind, string Key, int Attempt, string Directory, string StorageRoot);
    public sealed record Relocation(string SourceRoot, string DestinationRoot);
    public sealed record Relocations(int Version, IReadOnlyList<Relocation> Mappings);

    /// <summary>Inspect a pinned attempt without creating a locator, directory or fallback workspace.</summary>
    internal async Task<string?> ReadExistingAsync(string kind, string key, int attempt, CancellationToken ct) =>
        (await InspectExistingAsync(kind, key, attempt, ct))?.Directory;

    internal async Task<Location?> InspectExistingAsync(string kind, string key, int attempt, CancellationToken ct)
    {
        var found = await ReadLocationAsync(kind, key, attempt, ct);
        if (found == null) return null;
        var directory = Relocate(found.Directory);
        var root = Relocate(found.StorageRoot);
        RequireExisting(root);
        RequireExisting(directory);
        return found with {Directory = directory, StorageRoot = root};
    }

    private async Task<Location?> ReadLocationAsync(string kind, string key, int attempt, CancellationToken ct)
    {
        if (kind is not ("operations" or "checkpoints") || key.Length != 64 || key.Any(c => !Uri.IsHexDigit(c)) || attempt < 1)
            throw new ArgumentException("The processing workspace identity is invalid.");
        var locationFile = Path.Combine(MetadataRoot, "locations", $"{kind}-{key}-{attempt}.json");
        ArtifactManifestFiles.RejectLinks(locationFile);
        if (!File.Exists(locationFile)) return null;
        if (new FileInfo(locationFile).Length > 64 * 1024) throw new InvalidDataException("The workspace locator is too large.");
        var found = JsonSerializer.Deserialize<Location>(await File.ReadAllTextAsync(locationFile, ct), Json)
            ?? throw new InvalidDataException("The processing workspace locator is invalid.");
        if (found.Version != 1 || found.Kind != kind || found.Key != key || found.Attempt != attempt)
            throw new InvalidDataException("The processing workspace locator belongs to another operation.");
        var directory = Relocate(found.Directory);
        var root = Relocate(found.StorageRoot);
        FileProcessingFiles.Within(directory, root);
        storage?.EnsurePathAllowed(root);
        return found;
    }

    public static void ValidateConfiguredRoot(string? directory, IUserStoragePolicy? storage = null)
    {
        if (string.IsNullOrWhiteSpace(directory)) return;
        if (!Path.IsPathFullyQualified(directory)) throw new ArgumentException("Choose an absolute processing workspace directory.");
        ArtifactManifestFiles.RejectLinks(directory);
        storage?.EnsurePathAllowed(directory);
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException("The processing storage directory is unavailable. Reconnect it before continuing.");
    }

    public async Task<string> ResolveAsync(string kind, string key, int attempt, string inputRoot,
        string? configuredRoot, CancellationToken ct, ArtifactStoragePolicy? policy = null, string? title = null,
        bool newAttempt = false)
    {
        if (kind is not ("operations" or "checkpoints" or "rclone-copies") || key.Length != 64 || key.Any(c => !Uri.IsHexDigit(c)) || attempt < 1)
            throw new ArgumentException("The processing workspace identity is invalid.");
        await Gate.WaitAsync(ct);
        try
        {
            var locationFile = Path.Combine(MetadataRoot, "locations", $"{kind}-{key}-{attempt}.json");
            if (File.Exists(locationFile))
            {
                var found = JsonSerializer.Deserialize<Location>(await File.ReadAllTextAsync(locationFile, ct), Json)
                    ?? throw new InvalidDataException("The processing workspace locator is invalid.");
                if (found.Version != 1 || found.Kind != kind || found.Key != key || found.Attempt != attempt)
                    throw new InvalidDataException("The processing workspace locator belongs to another operation.");
                var directory = Relocate(found.Directory);
                var storageRoot = Relocate(found.StorageRoot);
                RequireExisting(storageRoot);
                // A missing pinned workspace is not an invitation to restart from downloaded inputs.
                RequireExisting(directory);
                return directory;
            }

            // A broad historical relocation is not evidence that this particular attempt
            // ever wrote there. Fresh claims durably choose their current storage before
            // touching it, so unrelated offline volumes cannot block new processing.
            var allocation = await ReadAllocationAsync(kind, key, attempt, ct);
            if (allocation == null && newAttempt)
                allocation = await SaveAllocationAsync(kind, key, attempt,
                    string.IsNullOrWhiteSpace(configuredRoot) ? DefaultRoot(inputRoot) : configuredRoot, ct);
            if (kind != "rclone-copies" && allocation == null)
            {
                var legacyBase = Path.Combine(appData, kind == "operations" ? "artifact-operations" : "artifact-plan-checkpoints");
                var legacy = Path.Combine(legacyBase, key, attempt.ToString());
                var relocated = Relocate(legacy);
                if (!ArtifactManifestFiles.PathComparer.Equals(legacy, relocated))
                {
                    RequireExisting(Relocate(legacyBase));
                    if (Directory.Exists(relocated))
                    {
                        RequireExisting(relocated);
                        await SaveLocation(locationFile, new(1, kind, key, attempt, relocated, Relocate(legacyBase)), ct);
                        return relocated;
                    }
                }
                if (Directory.Exists(legacy))
                {
                    ArtifactManifestFiles.RejectLinks(legacy);
                    await SaveLocation(locationFile, new(1, kind, key, attempt, legacy, legacyBase), ct);
                    return legacy;
                }

            }

            var selected = allocation == null
                ? string.IsNullOrWhiteSpace(configuredRoot) ? DefaultRoot(inputRoot) : Path.GetFullPath(configuredRoot)
                : Relocate(allocation.StorageRoot);
            ValidateConfiguredRoot(selected, storage);
            // The installation namespace prevents another library using the same download directory
            // from adopting or collecting these files. Its identity is retained when AppData moves.
            var ownerFile = Path.Combine(MetadataRoot, "owner.json");
            Directory.CreateDirectory(MetadataRoot);
            ArtifactManifestFiles.RejectLinks(MetadataRoot);
            string owner;
            ArtifactManifestFiles.RejectLinks(ownerFile);
            if (File.Exists(ownerFile)) owner = JsonSerializer.Deserialize<string>(await File.ReadAllTextAsync(ownerFile, ct), Json) ?? "";
            else
            {
                owner = Guid.NewGuid().ToString("N");
                await File.WriteAllTextAsync(ownerFile, JsonSerializer.Serialize(owner, Json), ct);
            }
            if (owner.Length != 32 || owner.Any(c => !Uri.IsHexDigit(c)))
                throw new InvalidDataException("The processing workspace owner is invalid.");
            var root = Path.Combine(selected, ".bakabase-work", owner);
            ArtifactManifestFiles.RejectLinks(root);
            var marker = Path.Combine(root, "workspace-owner.json");
            if (Directory.Exists(root) && !File.Exists(marker) && Directory.EnumerateFileSystemEntries(root).Any())
                throw new InvalidDataException("The processing workspace contains unrecognized files; it was retained.");
            Directory.CreateDirectory(root);
            if (File.Exists(marker))
            {
                if (await File.ReadAllTextAsync(marker, ct) != owner)
                    throw new InvalidDataException("The processing workspace belongs to another library.");
            }
            else await File.WriteAllTextAsync(marker, owner, ct);
            var relative = policy == null ? Path.Combine(kind, key, attempt.ToString())
                : Path.Combine(kind, ArtifactStoragePolicies.RenderProcessing(policy, title, key, kind, attempt));
            var result = FileProcessingFiles.Within(Path.Combine(root, relative), root);
            ArtifactManifestFiles.RejectLinks(result);
            if (Directory.Exists(result) && Directory.EnumerateFileSystemEntries(result).Any())
                throw new InvalidDataException("The new processing workspace contains untracked files; it was retained.");
            Directory.CreateDirectory(result);
            await SaveLocation(locationFile, new(1, kind, key, attempt, result, selected), ct);
            return result;
        }
        finally { Gate.Release(); }
    }

    private static string DefaultRoot(string inputRoot)
    {
        var input = Path.GetFullPath(inputRoot);
        // A processed artifact used as another input must not recursively create workspaces.
        for (var ancestor = new DirectoryInfo(input); ancestor != null; ancestor = ancestor.Parent)
            if (ancestor.Name == ".bakabase-work" && ancestor.Parent != null)
                return ancestor.Parent.FullName;
        return input;
    }

    private static void RequireExisting(string directory)
    {
        if (!Path.IsPathFullyQualified(directory)) throw new InvalidDataException("The stored workspace path is not absolute.");
        ArtifactManifestFiles.RejectLinks(directory);
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException("A pinned processing workspace is unavailable. Restore its storage before continuing: " + directory);
    }

    private static async Task SaveLocation(string path, Location location, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        ArtifactManifestFiles.RejectLinks(path);
        var pending = path + ".pending";
        await File.WriteAllTextAsync(pending, JsonSerializer.Serialize(location, Json), ct);
        File.Move(pending, path, true);
    }

    /// <summary>Migration maps are exact directory-prefix aliases; no arbitrary substring rewriting.</summary>
    public string Relocate(string path) => Map(path, reverse: false);
    /// <summary>Old absolute paths remain the stable identity of a migrated checkpoint.</summary>
    public string IdentityPath(string path) => Map(path, reverse: true);
    private string Map(string path, bool reverse)
    {
        foreach (var mapping in ReadRelocations())
        {
            var from = reverse ? mapping.DestinationRoot : mapping.SourceRoot;
            var to = reverse ? mapping.SourceRoot : mapping.DestinationRoot;
            if (Contains(from, path)) return Path.GetFullPath(Path.Combine(to, Path.GetRelativePath(from, path)));
        }
        return path;
    }

    /// <summary>Read the same bounded, validated alias snapshot used by workspace resolution.</summary>
    internal IReadOnlyList<Relocation> ReadRelocations()
    {
        var file = Path.Combine(MetadataRoot, "relocations.json");
        if (!File.Exists(file)) return [];
        ArtifactManifestFiles.RejectLinks(file);
        // Read a bounded snapshot each time. Avoid an indefinitely stale path cache when an
        // audited offline relocation atomically replaces this metadata file.
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > MaximumRelocationBytes) throw new InvalidDataException("The workspace relocation map is too large.");
        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        if (stream.ReadByte() != -1) throw new InvalidDataException("The workspace relocation map changed while being read.");
        var map = JsonSerializer.Deserialize<Relocations>(bytes, Json);
        if (map is not {Version: 1} || map.Mappings == null || map.Mappings.Count > MaximumRelocationMappings || map.Mappings.Any(m => m == null))
            throw new InvalidDataException("The workspace relocation map is invalid.");
        var mappings = map.Mappings.Select(m => new Relocation(Absolute(m.SourceRoot), Absolute(m.DestinationRoot))).ToList();
        var destinations = mappings.Select(m => m.DestinationRoot).ToHashSet(ArtifactManifestFiles.PathComparer);
        if (mappings.Any(m => Contains(m.SourceRoot, m.DestinationRoot) || Contains(m.DestinationRoot, m.SourceRoot)) ||
            OverlappingRoots(mappings.Select(m => m.SourceRoot)) ||
            OverlappingRoots(mappings.Select(m => m.DestinationRoot)) ||
            mappings.Any(m => HasMappedDestinationAncestor(m.SourceRoot, destinations)))
            throw new InvalidDataException("The workspace relocation map contains overlapping or cyclic roots.");
        return mappings;
    }
    private static bool OverlappingRoots(IEnumerable<string> roots)
    {
        // Sort with a trailing separator: /a-z must not hide /a/child from its ancestor /a.
        var ordered = roots.OrderBy(p => Path.EndsInDirectorySeparator(p) ? p : p + Path.DirectorySeparatorChar,
            ArtifactManifestFiles.PathComparer).ToArray();
        for (var index = 1; index < ordered.Length; index++)
            if (Contains(ordered[index - 1], ordered[index])) return true;
        return false;
    }
    private static bool HasMappedDestinationAncestor(string source, HashSet<string> destinations)
    {
        for (string? ancestor = source; ancestor != null; ancestor = Path.GetDirectoryName(ancestor))
            if (destinations.Contains(ancestor)) return true;
        return false;
    }
    private static string Absolute(string path)
    {
        var absolute = ArtifactManifestFiles.Absolute(path);
        if (ArtifactManifestFiles.PathComparer.Equals(absolute, Path.GetPathRoot(absolute)))
            throw new InvalidDataException("A workspace relocation cannot map an entire filesystem root.");
        return absolute;
    }
    private static bool Contains(string root, string path) => ArtifactManifestFiles.PathComparer.Equals(root, path) ||
        path.StartsWith(root + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
