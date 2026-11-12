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
    public static void ValidateDirectoryScope(ArtifactDirectoryScope scope)
    {
        static bool Identity(string text) => !string.IsNullOrWhiteSpace(text) && text.Length <= 128 &&
            text.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.');
        static bool Child(string root, string directory)
        {
            var relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(directory));
            return !Path.IsPathRooted(relative) && relative is not ("." or "..") &&
                !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
        }
        if (scope == null || !Identity(scope.OwnerKind) || !Identity(scope.OwnerId) || !Identity(scope.Role) ||
            string.IsNullOrWhiteSpace(scope.StorageRoot) || string.IsNullOrWhiteSpace(scope.Directory) ||
            scope.StorageRoot.Length > 4096 || scope.Directory.Length > 4096 ||
            scope.StorageRoot.Any(char.IsControl) || scope.Directory.Any(char.IsControl) ||
            !Path.IsPathFullyQualified(scope.StorageRoot) || !Path.IsPathFullyQualified(scope.Directory) ||
            !Child(scope.StorageRoot, scope.Directory) ||
            ArtifactManifestFiles.PathComparer.Equals(Path.GetFullPath(scope.StorageRoot), Path.GetFullPath(scope.Directory)))
            throw new ArgumentException("The task directory allocation is invalid.");
    }

    /// <summary>Allocates only an explicit child of available user storage. Receipts live in
    /// AppData, so user-visible payload folders contain no synthetic ownership files.</summary>
    public async Task<string> EnsureScopedDirectoryAsync(ArtifactDirectoryScope scope, CancellationToken ct)
    {
        ValidateDirectoryScope(scope);
        var normalized = scope with {StorageRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(scope.StorageRoot)),
            Directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(scope.Directory))};
        ValidateConfiguredRoot(normalized.StorageRoot, storage);
        storage?.EnsurePathAllowed(normalized.Directory);
        ArtifactManifestFiles.RejectLinks(normalized.Directory);
        var receipt = Path.Combine(MetadataRoot, "task-directories",
            ArtifactManifestFiles.Hash(normalized.Directory.ToUpperInvariant()) + ".json");
        await Gate.WaitAsync(ct);
        try
        {
            ArtifactManifestFiles.RejectLinks(receipt);
            if (File.Exists(receipt))
            {
                if (new FileInfo(receipt).Length > 32768 ||
                    JsonSerializer.Deserialize<ArtifactDirectoryScope>(await File.ReadAllTextAsync(receipt, ct), Json) != normalized)
                    throw new InvalidDataException("The task directory belongs to another allocation.");
                if (!Directory.Exists(normalized.Directory))
                    throw new DirectoryNotFoundException("The task directory is unavailable. Restore it before continuing.");
            }
            else
            {
                if (File.Exists(normalized.Directory) || Directory.Exists(normalized.Directory) &&
                    Directory.EnumerateFileSystemEntries(normalized.Directory).Any())
                    throw new InvalidDataException("The task directory already contains untracked files; choose another location.");
                Directory.CreateDirectory(Path.GetDirectoryName(receipt)!);
                ArtifactManifestFiles.RejectLinks(Path.GetDirectoryName(receipt)!);
                Directory.CreateDirectory(normalized.Directory);
                ArtifactManifestFiles.RejectLinks(normalized.Directory);
                var pending = receipt + ".pending";
                await File.WriteAllTextAsync(pending, JsonSerializer.Serialize(normalized, Json), ct);
                File.Move(pending, receipt, false);
            }
            ArtifactManifestFiles.RejectLinks(normalized.Directory);
            return normalized.Directory;
        }
        finally { Gate.Release(); }
    }
}
