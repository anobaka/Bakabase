using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Service.Components.FileProcessing;

namespace Bakabase.Service.Components.CloudDownloads.PikPakCli;

public sealed partial class PikPakCliCloudProvider : ICloudPreparedDownloadRetryProvider
{
    public async Task<CloudReadiness> CheckLocalRecoveryAsync(CloudDownloadRequest request, CancellationToken ct)
    {
        try
        {
            var output = PreparedRetryOutput(request);
            return await WithOperation(request.OperationId, async () =>
            {
                var copies = await ReadDownloadJournal(request.OperationId, ct);
                if (copies == null) return new CloudReadiness(true);
                if (copies is not {Started.Count: 0, Completed.Count: 0})
                    return new(false, "PIKPAK_PARTIAL_DOWNLOAD_REVIEW_REQUIRED");
                var saved = await ReadJournal(request.OperationId, ct);
                return saved is {State: "restored", Files.Count: > 0} &&
                       copies.Identity == DownloadIdentity(request, output, saved)
                    ? new(true) : new(false, "PIKPAK_OPERATION_CHANGED");
            }, ct);
        }
        catch (PikPakCliException error) { return new(false, error.Code); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        { return new(false, "PIKPAK_LOCAL_PATH_INVALID"); }
    }

    public async Task<CloudReadiness> CheckPreparedDownloadRetryAsync(CloudDownloadRequest request, CancellationToken ct)
    {
        try
        {
            var output = PreparedRetryOutput(request);
            return await WithOperation(request.OperationId, () => accounts.WithAccountAsync(
                request.AccountId ?? request.Source.AccountId, request.Source.AccountVersion, async account =>
                {
                    await InspectPreparedRetryAsync(request, output, account, verifyContent: false, ct);
                    return new CloudReadiness(true);
                }, ct), ct);
        }
        catch (PikPakCliException error) { return new(false, error.Code); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        { return new(false, "PIKPAK_LOCAL_PATH_INVALID"); }
    }

    public async Task<CloudDownloadResult> RetryPreparedDownloadAsync(CloudDownloadRequest request,
        Func<CloudDownloadProgress, Task> progress, CancellationToken ct)
    {
        try
        {
            var output = PreparedRetryOutput(request);
            return await WithOperation(request.OperationId, () => accounts.WithAccountAsync(
                request.AccountId ?? request.Source.AccountId, request.Source.AccountVersion, async account =>
                {
                    await account.RequireOnline(ct);
                    var (journal, copies) = await InspectPreparedRetryAsync(request, output, account, verifyContent: true, ct);
                    if (!Same(journal.Files, await ReadRemote(account, journal.FolderId!, ct)) ||
                        !MatchesSelection(journal.Selection, journal.Files))
                        throw new PikPakCliException("PIKPAK_REMOTE_CHANGED");
                    // Repeat the path proof after the provider lookup and before creating any payload.
                    await VerifyPreparedRetryLocationAsync(output, journal, copies, verifyContent: false, ct);
                    return await DownloadFiles(request, output, journal, account, progress, ct,
                        retryAttempt: "retry-" + Guid.NewGuid().ToString("N"));
                }, ct), ct);
        }
        catch (PikPakCliException error) { throw new CloudProviderException(error.Code, true); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        { throw new CloudProviderException("PIKPAK_LOCAL_PATH_INVALID", true); }
    }

    private string PreparedRetryOutput(CloudDownloadRequest request)
    {
        Validate(request.Source);
        if (request.RemoteEntry != null || string.IsNullOrWhiteSpace(request.OperationId) ||
            !Path.IsPathFullyQualified(request.OutputDirectory))
            throw new PikPakCliException("PIKPAK_OPERATION_CHANGED");
        var output = Path.GetFullPath(request.OutputDirectory);
        PikPakCliStorage.NoLinks(output);
        storage?.EnsurePathAllowed(output);
        return output;
    }

    private async Task<(Journal Journal, DownloadJournal Copies)> InspectPreparedRetryAsync(CloudDownloadRequest request,
        string output, PikPakCliAccountLease account, bool verifyContent, CancellationToken ct)
    {
        var journal = await ReadJournal(request.OperationId, ct);
        if (journal is not {State: "restored", FolderId.Length: > 0, Files.Count: > 0})
            throw new PikPakCliException("PIKPAK_RESTORE_REVIEW_REQUIRED");
        CheckIdentity(journal, request.Source, account);
        ValidateId(journal.FolderId);
        var copies = await ReadDownloadJournal(request.OperationId, ct);
        if (copies == null || copies.Identity != DownloadIdentity(request, output, journal))
            throw new PikPakCliException("PIKPAK_OPERATION_CHANGED");
        if (copies.Started == null || copies.Completed == null ||
            !copies.Started.IsSubsetOf(journal.Files.Select(file => file.Id)) ||
            !copies.Completed.Keys.ToHashSet(StringComparer.Ordinal).IsSubsetOf(journal.Files.Select(file => file.Path)) ||
            !journal.Files.Any(file => copies.Started.Contains(file.Id) && !copies.Completed.ContainsKey(file.Path)))
            throw new PikPakCliException("PIKPAK_PARTIAL_DOWNLOAD_REVIEW_REQUIRED");
        await VerifyPreparedRetryLocationAsync(output, journal, copies, verifyContent, ct);
        return (journal, copies);
    }

    private Task<DownloadJournal?> ReadDownloadJournal(string operationId, CancellationToken ct) =>
        PikPakCliStorage.ReadAsync<DownloadJournal>(Path.Combine(OperationPath(operationId), "download.json"), ct);

    private static async Task VerifyPreparedRetryLocationAsync(string output, Journal journal, DownloadJournal copies,
        bool verifyContent, CancellationToken ct)
    {
        var planned = journal.Files.ToDictionary(file => PikPakCliStorage.Within(output, file.Path),
            ArtifactManifestFiles.PathComparer);
        var verified = new HashSet<string>(ArtifactManifestFiles.PathComparer);
        var pending = new Stack<(string Path, int Depth)>();
        pending.Push((output, 0));
        var entries = 0;
        while (pending.TryPop(out var directory))
        {
            ct.ThrowIfCancellationRequested();
            if (directory.Depth > 32 ||
                (File.GetAttributes(directory.Path) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != FileAttributes.Directory)
                throw new PikPakCliException("PIKPAK_LOCAL_PATH_INVALID");
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory.Path))
            {
                ct.ThrowIfCancellationRequested();
                if (++entries > 10_000) throw new PikPakCliException("PIKPAK_LOCAL_PATH_INVALID");
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new PikPakCliException("PIKPAK_LOCAL_PATH_INVALID");
                var member = planned.GetValueOrDefault(entry);
                if ((attributes & FileAttributes.Directory) != 0 && member == null)
                {
                    pending.Push((entry, directory.Depth + 1));
                    continue;
                }
                ArtifactFileIdentity.RequireRegularFile(entry);
                if (member == null && FileProcessingFiles.IsDarwinSmbDeleteMarker(Path.GetFileName(entry))) continue;
                if (member == null || !copies.Completed.TryGetValue(member.Path, out var receipt))
                    throw new PikPakCliException("PIKPAK_PARTIAL_DOWNLOAD_REVIEW_REQUIRED");
                if (receipt == null || receipt.Size != member.Size || new FileInfo(entry).Length != receipt.Size ||
                    verifyContent && receipt.Sha256 != await PikPakCliStorage.FileHash(entry, ct))
                    throw new PikPakCliException("PIKPAK_LOCAL_CHANGED");
                verified.Add(member.Path);
            }
        }
        if (verified.Count != copies.Completed.Count) throw new PikPakCliException("PIKPAK_LOCAL_CHANGED");
    }
}
