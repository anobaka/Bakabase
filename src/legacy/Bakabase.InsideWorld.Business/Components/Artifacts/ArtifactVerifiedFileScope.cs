using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Modules.Workflow.Artifacts;

namespace Bakabase.InsideWorld.Business.Components.Artifacts;

// A conservative proof for concurrent, finite file operations. Ordinary storage and reference
// checks are still required. In particular, a volume capacity or a pathname is not file identity.
internal sealed class ArtifactVerifiedFileScope
{
    internal const int MaximumFiles = 4096;
    private readonly VerifiedStorageMount _mount;
    private readonly string _root;
    private readonly HashSet<string> _objects = new(StringComparer.Ordinal);
    private readonly HashSet<(long Length, string Sha256)> _contents = [];
    private ArtifactVerifiedFileScope(VerifiedStorageMount mount, string root) { _mount = mount; _root = root; }

    internal static ArtifactVerifiedFileScope? TryCapture(string root, ArtifactManifest manifest,
        IReadOnlyList<string> files, CancellationToken ct, Func<string, IReadOnlyList<string>, VerifiedStorageMount?>? mountIdentity = null)
    {
        if (files.Count is 0 or > MaximumFiles || manifest.Entries.Count is 0 or > MaximumFiles) return null;
        try
        {
            if (ArtifactManifestFiles.Fingerprint(manifest.Entries, manifest.Directories) != manifest.Fingerprint ||
                !Path.IsPathFullyQualified(root) || Path.GetFullPath(root) != root ||
                files.Any(path => !Path.IsPathFullyQualified(path) || Path.GetFullPath(path) != path)) return null;
            ArtifactManifestFiles.RejectLinks(root);
            var mount = (mountIdentity ?? StorageVolumes.VerifiedFileMount)(root, files);
            if (mount == null) return null;
            var scope = new ArtifactVerifiedFileScope(mount, Path.TrimEndingDirectorySeparator(root));
            var paths = ArtifactManifestFiles.ResolveFiles(root, manifest);
            var entries = paths.Zip(manifest.Entries).ToDictionary(pair => pair.First, pair => pair.Second, ArtifactManifestFiles.PathComparer);
            foreach (var path in files)
            {
                ct.ThrowIfCancellationRequested();
                if (!entries.TryGetValue(path, out var entry) || entry.Sha256.Length != 64 || !entry.Sha256.All(Uri.IsHexDigit) ||
                    new FileInfo(path).Length != entry.Length || !scope._objects.Add(ArtifactFileIdentity.Read(path))) return null;
                scope._contents.Add((entry.Length, entry.Sha256.ToUpperInvariant()));
            }
            return scope;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    internal bool IsIndependentOf(ArtifactVerifiedFileScope other) =>
        _mount.Identity != other._mount.Identity && _mount.NativeFileIdentities == other._mount.NativeFileIdentities &&
        !UnderOrSame(_root, other._root) && !UnderOrSame(other._root, _root) &&
        !_objects.Overlaps(other._objects) &&
        // Forwarded namespaces can give the same remote object different inode numbers.
        // On separate native filesystems identical bytes, including empty files, are independent.
        (_mount.NativeFileIdentities || !_contents.Overlaps(other._contents));

    private static bool UnderOrSame(string path, string root) => path.Equals(root, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);
}
