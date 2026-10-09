using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Bakabase.Service.Components.ServerData;

/// <summary>The import's durable, reviewed inputs. Never contains a database connection or setup capability.</summary>
public sealed record ImportPathPlan(string SourceFingerprint, PathMappingRule[] Rules, string? DraftId = null);

public sealed record SetupImportDraft(string Id, DateTimeOffset UpdatedAt,
    ServerSetupSession.SetupRequest Request, PathMappingRule[] Rules);
public sealed record SetupDraftResult(SetupImportDraft? Draft, string? Error = null);
public sealed record SetupDraftRequest(ServerSetupSession.SetupRequest Request, PathMappingRule[]? Rules = null);
public sealed record SetupPreviewRequest(string ScanId, PathMappingRule[]? Rules = null);
public sealed record SetupPathNode(string Path, string Name, long ReferenceCount, bool HasChildren);
public sealed record SetupPathTree(SetupPathNode[] Nodes, int Total, int Offset, int Limit = 200);
public sealed record SetupPreflightStatus(string? Id = null, string Phase = "idle", PathMappingProgress? Progress = null,
    string? Error = null, int UniquePaths = 0, long ReferenceCount = 0, string? SourcePath = null, string? TargetPath = null);
public sealed record SetupPathPreview(string PreviewId, int MatchedPaths, long MatchedReferences,
    int UnmappedPaths, long UnmappedReferences, IReadOnlyList<PathMappingExample> Examples, string[] Warnings);

/// <summary>Small, private, atomic JSON draft. The source data and browser storage are never used for drafts.</summary>
public static class SetupImportDraftStore
{
    public const string FileName = ".bakabase-setup-draft.json";
    private const int MaxDraftBytes = 2 * 1024 * 1024;
    private static readonly object Gate = new();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static SetupDraftResult Read(string anchor)
    {
        lock (Gate)
        {
            try
            {
                var path = Path.Combine(anchor, FileName);
                RejectLink(path);
                if (!File.Exists(path)) return new(null);
                if (new FileInfo(path).Length > MaxDraftBytes) throw new IOException("The saved setup draft is too large. Clear it to start again.");
                var draft = JsonSerializer.Deserialize<SetupImportDraft>(File.ReadAllText(path), Json);
                if (draft == null || !Guid.TryParseExact(draft.Id, "N", out _) || draft.Request == null || draft.Rules == null)
                    throw new IOException("The saved setup draft is invalid. Clear it to start again.");
                ValidateSize(draft.Request, draft.Rules);
                return new(draft);
            }
            catch (Exception error) when (error is IOException or JsonException or ArgumentException or UnauthorizedAccessException)
            {
                return new(null, error.Message);
            }
        }
    }

    public static SetupDraftResult Save(string anchor, SetupDraftRequest request)
    {
        lock (Gate)
        {
            var rules = request.Rules ?? [];
            ValidateSize(request.Request, rules);
            var previous = Read(anchor);
            if (previous.Error != null) throw new IOException(previous.Error);
            var draft = new SetupImportDraft(previous.Draft?.Id ?? Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow,
                SelectionOnly(request.Request), rules.ToArray());
            var bytes = JsonSerializer.SerializeToUtf8Bytes(draft, Json);
            if (bytes.Length > MaxDraftBytes) throw new ArgumentException("The setup draft is too large.");
            Directory.CreateDirectory(anchor);
            var path = Path.Combine(anchor, FileName);
            RejectLink(path); RejectLink(path + ".tmp");
            File.Delete(path + ".tmp");
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (var stream = new FileStream(path + ".tmp", options)) { stream.Write(bytes); stream.Flush(true); }
            File.Move(path + ".tmp", path, true);
            return new(draft);
        }
    }

    public static void Clear(string anchor, string? onlyDraftId = null)
    {
        lock (Gate)
        {
            if (onlyDraftId != null && Read(anchor).Draft?.Id != onlyDraftId) return;
            var path = Path.Combine(anchor, FileName);
            RejectLink(path); RejectLink(path + ".tmp");
            File.Delete(path); File.Delete(path + ".tmp");
        }
    }

    internal static ServerSetupSession.SetupRequest SelectionOnly(ServerSetupSession.SetupRequest request) => new()
    {
        SourcePath = request.SourcePath, TargetPath = request.TargetPath,
        OriginalDataPath = request.OriginalDataPath, Operation = request.Operation
    };

    private static void ValidateSize(ServerSetupSession.SetupRequest request, IReadOnlyList<PathMappingRule> rules)
    {
        if (request == null || rules.Count > 256) throw new ArgumentException("A draft supports up to 256 path mappings.");
        foreach (var value in new[] { request.SourcePath, request.TargetPath, request.OriginalDataPath, request.Operation }
                     .Concat(rules.SelectMany(rule => rule == null ? new string?[] { new string('x', 4097) } : [rule.SourcePrefix, rule.TargetPrefix])))
            if (value?.Length > 4096 || value?.Contains('\0') == true)
                throw new ArgumentException("A setup path is too long or contains a null character.");
    }

    private static void RejectLink(string path)
    {
        if (new FileInfo(path).LinkTarget != null) throw new IOException("Setup draft files cannot be symbolic links.");
    }
}

/// <summary>Read-only asynchronous preflight, shared by desktop and server Setup; no business host is constructed.</summary>
public sealed class SetupImportPreflight : IDisposable
{
    private readonly object _gate = new();
    private readonly string _anchor;
    private CancellationTokenSource? _cancel;
    private SetupPreflightStatus _status = new();
    private ServerSetupSession.SetupRequest? _selection;
    private PathMappingScan? _scan;
    private string? _fingerprint;
    private string? _inputStamp;
    private string? _previewId;
    private PathMappingRule[]? _previewRules;
    private PathTreeIndex? _tree;
    private bool _disposed;

    public SetupImportPreflight(string anchor) => _anchor = anchor;

    public SetupPreflightStatus Read() { lock (_gate) return _status; }

    public SetupPreflightStatus Start(ServerSetupSession.SetupRequest selection, bool force = false)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!force && (_status.Phase is "scanning" or "ready") && SameSelection(_selection, selection)) return _status;
            _cancel?.Cancel();
            _cancel = new CancellationTokenSource();
            var cancellation = _cancel.Token;
            _selection = SetupImportDraftStore.SelectionOnly(selection);
            _scan = null; _tree = null; _fingerprint = null; _inputStamp = null; _previewId = null; _previewRules = null;
            var id = Guid.NewGuid().ToString("N");
            _status = new(id, "scanning", SourcePath: selection.SourcePath, TargetPath: selection.TargetPath);
            var source = selection.SourcePath!;
            _ = Task.Run(() => Scan(id, source, cancellation));
            return _status;
        }
    }

    private void Scan(string id, string source, CancellationToken cancellation)
    {
        try
        {
            using var held = ServerAppDataImport.OpenSourceLock(source);
            void Progress(PathMappingProgress progress)
            {
                lock (_gate) if (!_disposed && _status.Id == id) _status = _status with { Progress = progress };
            }
            var before = Fingerprint(source, cancellation);
            var scan = AppDataPathMapping.Scan(source, cancellation, Progress);
            var fingerprint = Fingerprint(source, cancellation);
            if (before != fingerprint) throw new IOException("The source changed during preflight. Stop its Bakabase instance and scan again.");
            var stamp = InputStamp(source);
            var tree = new PathTreeIndex(scan, cancellation);
            lock (_gate)
            {
                if (_disposed || cancellation.IsCancellationRequested || _status.Id != id) return;
                _scan = scan; _tree = tree; _fingerprint = fingerprint; _inputStamp = stamp;
                _status = _status with { Phase = "ready", UniquePaths = scan.Paths.Count,
                    ReferenceCount = scan.Paths.Sum(path => path.ReferenceCount) };
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            lock (_gate) if (!_disposed && _status.Id == id)
                _status = _status with { Phase = "failed", Error = error.Message };
        }
    }

    public SetupPathTree Tree(string scanId, string? parent, int offset)
    {
        PathTreeIndex tree;
        lock (_gate)
        {
            RequireReady(scanId);
            if (offset < 0 || parent?.Length > 4096) throw new ArgumentException("Invalid path-tree page.");
            tree = _tree!;
        }
        var page = tree.Read(parent, offset);
        lock (_gate) RequireReady(scanId);
        return page;
    }

    public SetupPathPreview Preview(SetupPreviewRequest request)
    {
        PathMappingScan scan;
        PathMappingRule[] rules;
        lock (_gate)
        {
            RequireReady(request.ScanId);
            rules = AppDataPathMapping.ValidateRules(request.Rules ?? []).ToArray();
            if (rules.Length > 256) throw new ArgumentException("Use at most 256 path mappings.");
            scan = _scan!;
        }
        // Pure computation on an immutable snapshot. Do not probe remote destinations:
        // an unavailable NAS must not hold the session/status lock for an OS timeout.
        var preview = AppDataPathMapping.Preview(scan, rules);
        lock (_gate)
        {
            RequireReady(request.ScanId);
            if (!ReferenceEquals(scan, _scan)) throw new IOException("The path scan changed. Preview the mappings again.");
            _previewId = Guid.NewGuid().ToString("N"); _previewRules = rules;
            var warnings = new List<string>();
            if (preview.UnmappedReferences > 0) warnings.Add("Unmapped references will keep their original paths. Their files may be unavailable on this device.");
            if (rules.Length > 0)
                warnings.Add("Destination availability is not checked here. Confirm the server/container mounts using the folder browser; no media files will be moved or folders created.");
            return new(_previewId, preview.MatchedPaths, preview.MatchedReferences, preview.UnmappedPaths,
                preview.UnmappedReferences, preview.Examples, warnings.ToArray());
        }
    }

    public ImportPathPlan RequirePlan(ServerSetupSession.SetupRequest request)
    {
        lock (_gate)
        {
            RequireReady(request.PathPreflightId ?? "");
            if (!SameSelection(_selection, request) || request.PathPreviewId != _previewId || _previewRules == null ||
                !AppDataPathMapping.ValidateRules(request.PathMappings ?? []).SequenceEqual(_previewRules))
                throw new IOException("The import selection or mappings changed. Review the preflight again before importing.");
            if (InputStamp(_selection!.SourcePath!) != _inputStamp)
                throw new IOException("The source changed after preflight. Scan it again before importing.");
            return new(_fingerprint!, _previewRules.ToArray(), SetupImportDraftStore.Read(_anchor).Draft?.Id);
        }
    }

    private void RequireReady(string id)
    {
        if (_disposed || _scan == null || _status.Phase != "ready" || _status.Id != id)
            throw new IOException("Complete the import preflight before reviewing or applying path mappings.");
    }

    private static bool SameSelection(ServerSetupSession.SetupRequest? a, ServerSetupSession.SetupRequest b) =>
        a != null && a.SourcePath == b.SourcePath && a.TargetPath == b.TargetPath &&
        a.OriginalDataPath == b.OriginalDataPath && a.Operation == b.Operation;

    private static string InputStamp(string directory) => string.Join('\n', AppDataPathMapping.InputFiles(directory)
        .OrderBy(path => path, StringComparer.Ordinal).Select(path =>
        {
            var file = new FileInfo(path);
            return path + "\0" + file.Length + "\0" + file.LastWriteTimeUtc.Ticks;
        }));

    public static string Fingerprint(string directory, CancellationToken cancellation = default)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[1024 * 1024];
        foreach (var path in AppDataPathMapping.InputFiles(directory).OrderBy(path => path, StringComparer.Ordinal))
        {
            cancellation.ThrowIfCancellationRequested();
            var name = Encoding.UTF8.GetBytes(Path.GetRelativePath(directory, path));
            hash.AppendData(BitConverter.GetBytes(name.Length)); hash.AppendData(name);
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            hash.AppendData(BitConverter.GetBytes(input.Length));
            int count;
            while ((count = input.Read(buffer)) > 0) { cancellation.ThrowIfCancellationRequested(); hash.AppendData(buffer, 0, count); }
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    internal static void ValidatePlan(ImportPathPlan? plan)
    {
        if (plan == null) return;
        if (plan.SourceFingerprint is not { Length: 64 } || !plan.SourceFingerprint.All(Uri.IsHexDigit) ||
            plan.Rules == null || plan.Rules.Length > 256 ||
            plan.DraftId != null && !Guid.TryParseExact(plan.DraftId, "N", out _))
            throw new IOException("The saved path-mapping plan is invalid.");
        AppDataPathMapping.ValidateRules(plan.Rules);
    }

    // Keep one index of references to the scan's path strings. Building a node for
    // every stored file duplicates a large library's strings and empty child maps.
    // Only the currently requested directory is grouped; pages never materialize
    // the rest of the tree or perform remote filesystem calls.
    private sealed class PathTreeIndex
    {
        private readonly PathMappingReference[] _paths;
        private readonly SetupPathNode[] _roots;
        public PathTreeIndex(PathMappingScan scan, CancellationToken cancellation)
        {
            _paths = scan.Paths.ToArray();
            Array.Sort(_paths, (left, right) => Compare(left.Path, right.Path));
            var roots = new Dictionary<string, NodeCount>(StringComparer.OrdinalIgnoreCase);
            foreach (var reference in _paths)
            {
                cancellation.ThrowIfCancellationRequested();
                var path = reference.Path;
                string root;
                if (path.Length >= 3 && path[1] == ':') root = path[..3];
                else if (path.StartsWith("//", StringComparison.Ordinal))
                {
                    var serverEnd = path.IndexOf('/', 2);
                    var shareEnd = serverEnd < 0 ? -1 : path.IndexOf('/', serverEnd + 1);
                    root = shareEnd < 0 ? path : path[..shareEnd];
                }
                else root = "/";
                if (!roots.TryGetValue(root, out var node)) roots[root] = node = new(root, root);
                node.Count += reference.ReferenceCount;
                node.Children |= !path.Equals(root, Comparison(root));
            }
            _roots = roots.Values.OrderBy(node => node.Path, StringComparer.OrdinalIgnoreCase).Select(node => node.ToResult()).ToArray();
        }

        public SetupPathTree Read(string? parent, int offset)
        {
            if (string.IsNullOrEmpty(parent)) return new(_roots.Skip(offset).Take(200).ToArray(), _roots.Length, offset);
            var prefix = parent.TrimEnd('/') + "/";
            var comparison = Comparison(prefix);
            var low = 0; var high = _paths.Length;
            while (low < high)
            {
                var mid = low + (high - low) / 2;
                if (Compare(_paths[mid].Path, prefix) < 0) low = mid + 1; else high = mid;
            }
            var children = new Dictionary<string, NodeCount>(Windows(prefix) ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            for (var i = low; i < _paths.Length; i++)
            {
                var item = _paths[i];
                if (!item.Path.StartsWith(prefix, comparison)) break;
                // POSIX root does not own UNC shares; they have their own tree roots.
                if (prefix == "/" && Windows(item.Path)) continue;
                var remaining = item.Path.AsSpan(prefix.Length);
                if (remaining.IsEmpty) continue;
                var separator = remaining.IndexOf('/');
                var name = (separator < 0 ? remaining : remaining[..separator]).ToString();
                if (!children.TryGetValue(name, out var node))
                    children[name] = node = new(separator < 0 ? item.Path : prefix + name, name);
                node.Count += item.ReferenceCount;
                node.Children |= separator >= 0;
            }
            var page = children.Values.OrderBy(node => node.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(node => node.Name, StringComparer.Ordinal).Skip(offset).Take(200).Select(node => node.ToResult()).ToArray();
            return new(page, children.Count, offset);
        }

        private sealed class NodeCount(string path, string name)
        {
            public string Path { get; } = path;
            public string Name { get; } = name;
            public long Count;
            public bool Children;
            public SetupPathNode ToResult() => new(Path, Name, Count, Children);
        }
        private static bool Windows(string path) => path.StartsWith("//", StringComparison.Ordinal) || path.Length >= 3 && path[1] == ':';
        private static StringComparison Comparison(string path) => Windows(path) ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        private static int Compare(string left, string right) => string.Compare(left, right,
            Windows(left) && Windows(right) ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    public void Dispose()
    {
        lock (_gate) { _disposed = true; _cancel?.Cancel(); _scan = null; _tree = null; }
    }
}
