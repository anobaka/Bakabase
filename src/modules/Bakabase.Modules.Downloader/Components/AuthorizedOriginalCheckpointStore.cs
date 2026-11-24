using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Bakabase.Modules.Downloader.Models;
using Bakabase.Abstractions.Components.FileSystem;

namespace Bakabase.Modules.Downloader.Components;

/// <summary>One private member directory and one append-only digest journal. Only complete,
/// flushed segments are acknowledged, so a process exit costs at most the in-flight range.</summary>
internal sealed class AuthorizedOriginalCheckpointStore
{
    internal const int SegmentBytes = 8 * 1024 * 1024;
    private const int DigestSize = 32;
    private const byte PublishedSeal = 0xa5;
    private const int HeaderSize = 4 + 2 * DigestSize;
    private static ReadOnlySpan<byte> Magic => "BOR1"u8;
    private readonly byte[] _key;
    private readonly long _length;
    internal string DirectoryPath { get; }
    internal string Partial => Path.Combine(DirectoryPath, "payload.download");
    internal string Payload => Path.Combine(DirectoryPath, "payload");
    private string Ledger => Path.Combine(DirectoryPath, "checkpoint");
    private string NewLedger => Path.Combine(DirectoryPath, "checkpoint.new");
    private string LockFile => Path.Combine(DirectoryPath, "transfer.lock");
    internal sealed record State(byte[] Version, byte[] Digests, long VerifiedBytes, bool Published);

    internal AuthorizedOriginalCheckpointStore(AuthorizedOriginalIdentity identity, string directory)
    {
        var keys = new[] { identity.Provider, identity.OperationId, identity.AccountVersion, identity.FolderId,
            identity.MemberId, identity.ImmutableId, identity.AttemptScope };
        if (identity.Length < 0 || keys.Any(key => string.IsNullOrWhiteSpace(key) || key.Length > 4096 ||
            key.Contains("://", StringComparison.Ordinal) || key.Any(char.IsControl)) ||
            !Path.IsPathFullyQualified(directory)) throw Invalid();
        _key = SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(identity));
        _length = identity.Length;
        DirectoryPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        if (DirectoryPath == Path.GetPathRoot(DirectoryPath)) throw Invalid();
        for (var ancestor = new DirectoryInfo(DirectoryPath); ancestor != null; ancestor = ancestor.Parent)
            if (ancestor.LinkTarget != null || ancestor.Exists && ancestor.Attributes.HasFlag(FileAttributes.ReparsePoint))
                throw Invalid();
        if (File.Exists(DirectoryPath)) throw Invalid();
    }

    internal void EnsureDirectory()
    {
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(DirectoryPath);
        else Directory.CreateDirectory(DirectoryPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    private void ValidateEntries()
    {
        foreach (var entry in new DirectoryInfo(DirectoryPath).EnumerateFileSystemInfos())
        {
            if (entry.Name is not ("transfer.lock" or "checkpoint.new" or "checkpoint" or "payload.download" or "payload") ||
                entry.LinkTarget != null || (entry.Attributes & (FileAttributes.Device | FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
                throw Invalid();
            AuthorizedOriginalCheckpointFile.RequirePrivateRegularFile(entry.FullName, entry.Name == "transfer.lock");
            if (entry.Name == "transfer.lock" && ((FileInfo)entry).Length != 0) throw Invalid();
        }
    }

    internal FileStream Lease(bool create)
    {
        ValidateEntries();
        try
        {
            return new FileStream(LockFile, create ? FileMode.OpenOrCreate : FileMode.Open,
                create ? FileAccess.ReadWrite : FileAccess.Read, FileShare.None);
        }
        catch (IOException) when (File.Exists(LockFile)) { throw new AuthorizedOriginalDownloadException("ORIGINAL_HTTP_BUSY"); }
    }

    internal bool HasEntries => Directory.Exists(DirectoryPath) &&
        Directory.EnumerateFileSystemEntries(DirectoryPath).Any();

    internal async Task<State?> ReadAsync(CancellationToken ct, bool handedOff = false)
    {
        ValidateEntries();
        if (!File.Exists(Ledger))
        {
            if (handedOff || File.Exists(Partial) || File.Exists(Payload)) throw Invalid();
            // Initialization writes and flushes this small header before its atomic rename.
            if (File.Exists(NewLedger) && new FileInfo(NewLedger).Length > HeaderSize) throw Invalid();
            return null;
        }
        if (File.Exists(NewLedger)) throw Invalid();
        var size = new FileInfo(Ledger).Length;
        var segmentCount = _length == 0 ? 0 : 1 + (_length - 1) / SegmentBytes;
        if (size < HeaderSize || (size - HeaderSize) / DigestSize > segmentCount || size > 64 * 1024 * 1024)
            throw Invalid();
        var data = await File.ReadAllBytesAsync(Ledger, ct);
        if (!MatchesHeader(data)) throw Invalid();
        var count = (data.Length - HeaderSize) / DigestSize;
        var verifiedBytes = Math.Min((long)count * SegmentBytes, _length);
        var journalEnd = HeaderSize + count * DigestSize;
        var sealedForHandoff = verifiedBytes == _length && data.Length == journalEnd + 1 && data[journalEnd] == PublishedSeal;
        if (verifiedBytes == _length && data.Length != journalEnd && !sealedForHandoff) throw Invalid();
        var published = File.Exists(Payload);
        if (handedOff)
        {
            if (published || File.Exists(Partial) || !sealedForHandoff) throw Invalid();
            return new State(data[4..(4 + DigestSize)], data[HeaderSize..journalEnd], verifiedBytes, false);
        }
        var path = published ? Payload : Partial;
        var actual = File.Exists(path) ? new FileInfo(path).Length : 0;
        if (actual < verifiedBytes || actual > _length || actual - verifiedBytes > SegmentBytes ||
            published && (File.Exists(Partial) || !sealedForHandoff)) throw Invalid();
        return new State(data[4..(4 + DigestSize)], data[HeaderSize..(HeaderSize + count * DigestSize)], verifiedBytes, published);
    }

    private bool MatchesHeader(byte[] data) => data.Length >= HeaderSize && data.AsSpan(0, 4).SequenceEqual(Magic) &&
        data.AsSpan(4 + DigestSize, DigestSize).SequenceEqual(_key);

    internal async Task<State> InitializeAsync(byte[] version, CancellationToken ct)
    {
        // This is the sole initialization file inside the exclusively leased private directory.
        if (File.Exists(NewLedger))
        {
            var previous = await File.ReadAllBytesAsync(NewLedger, ct);
            if (previous.Length != HeaderSize || !MatchesHeader(previous) ||
                !previous.AsSpan(4, DigestSize).SequenceEqual(version)) throw Invalid();
        }
        else
        {
            using var file = new FileStream(NewLedger, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await file.WriteAsync(Magic.ToArray(), ct);
            await file.WriteAsync(version, ct);
            await file.WriteAsync(_key, ct);
            file.Flush(flushToDisk: true);
        }
        File.Move(NewLedger, Ledger);
        return new State(version, [], 0, false);
    }

    internal async Task VerifyAsync(FileStream payload, State state, CancellationToken ct)
    {
        using var observation = FileIoObservationScope.Begin(read: true, write: false);
        for (var offset = 0; offset < state.Digests.Length; offset += DigestSize)
        {
            var start = (long)(offset / DigestSize) * SegmentBytes;
            var length = Math.Min(SegmentBytes, _length - start);
            var digest = await HttpDownloadCheckpoint.HashRangeAsync(payload, start, length, ct);
            observation?.Add(length, 0);
            if (!Convert.FromHexString(digest).AsSpan().SequenceEqual(state.Digests.AsSpan(offset, DigestSize)))
                throw Invalid();
        }
    }

    internal FileStream OpenWriter(State state)
    {
        var journal = new FileStream(Ledger, FileMode.Open, FileAccess.Write, FileShare.Read);
        // A process can die after writing part of a digest. Only the full records count.
        journal.Position = HeaderSize + state.Digests.Length;
        journal.SetLength(journal.Position);
        return journal;
    }

    internal void Publish()
    {
        using (var journal = new FileStream(Ledger, FileMode.Append, FileAccess.Write, FileShare.Read))
        {
            journal.WriteByte(PublishedSeal);
            journal.Flush(flushToDisk: true);
        }
        File.Move(Partial, Payload);
    }

    internal static byte[] Fingerprint(string tag) => SHA256.HashData(Encoding.UTF8.GetBytes(tag));
    internal static AuthorizedOriginalDownloadException Invalid() => new("ORIGINAL_HTTP_CHECKPOINT_INVALID");
}
