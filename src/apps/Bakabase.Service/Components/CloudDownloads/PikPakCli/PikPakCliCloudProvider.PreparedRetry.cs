using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Modules.Downloader.Models;
using Bakabase.Service.Components.FileProcessing;

namespace Bakabase.Service.Components.CloudDownloads.PikPakCli;

public sealed partial class PikPakCliCloudProvider : ICloudPreparedDownloadRetryProvider
{
    public bool CanRetryPreparedDownloadFailure(string failureCode) => failureCode == "PIKPAK_CLI_REQUEST_FAILED";

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

    public Task<CloudReadiness> CheckPreparedDownloadRetryAsync(CloudDownloadRequest request, CancellationToken ct) =>
        CheckPreparedDownloadRetryAsync(request, requireCommittedPayload: false, ct);

    public Task<CloudReadiness> CheckPausedPreparedDownloadAsync(CloudDownloadRequest request, CancellationToken ct) =>
        CheckPreparedDownloadRetryAsync(request, requireCommittedPayload: true, ct);

    private async Task<CloudReadiness> CheckPreparedDownloadRetryAsync(CloudDownloadRequest request,
        bool requireCommittedPayload, CancellationToken ct)
    {
        try
        {
            var output = PreparedRetryOutput(request);
            return await WithOperation(request.OperationId, async () =>
            {
                // Fresh or pre-transfer failures do not need to acquire the account lease.
                var copies = await ReadDownloadJournal(request.OperationId, ct);
                if (copies is not {Started.Count: > 0})
                    return new CloudReadiness(false, "PIKPAK_PARTIAL_DOWNLOAD_REVIEW_REQUIRED");
                return await accounts.WithAccountAsync(request.AccountId ?? request.Source.AccountId,
                    request.Source.AccountVersion, async account =>
                    {
                        await InspectPreparedRetryAsync(request, output, account, copies, verifyContent: false, ct,
                            requireCommittedPayload: requireCommittedPayload);
                        return new CloudReadiness(true);
                    }, ct);
            }, ct);
        }
        catch (PikPakCliException error) { return new(false, error.Code); }
        catch (AuthorizedOriginalDownloadException error) { return new(false, RestoredHttpProblem(error).Code); }
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
                    var copies = await ReadDownloadJournal(request.OperationId, ct);
                    var journal = await InspectPreparedRetryAsync(request, output, account, copies, verifyContent: true, ct,
                        PikPakPayloadActivity.ReportReads(() => progress(new(null, Stage: "Verifying"))));
                    if (!Same(journal.Files, await ReadRemote(account, journal.FolderId!, ct)) ||
                        !MatchesSelection(journal.Selection, journal.Files))
                        throw new PikPakCliException("PIKPAK_REMOTE_CHANGED");
                    // Repeat the path proof after the provider lookup and before creating any payload.
                    await VerifyPreparedRetryLocationAsync(request, output, journal, copies!, verifyContent: false, ct);
                    return await DownloadFiles(request, output, journal, account, progress, ct,
                        retryRestoredOriginal: true);
                }, ct), ct);
        }
        catch (PikPakCliException error) { throw new CloudProviderException(error.Code, true); }
        catch (AuthorizedOriginalDownloadException error) { throw new CloudProviderException(RestoredHttpProblem(error).Code, true); }
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

    private async Task<Journal> InspectPreparedRetryAsync(CloudDownloadRequest request, string output,
        PikPakCliAccountLease account, DownloadJournal? copies, bool verifyContent, CancellationToken ct,
        Func<ValueTask>? onVerificationRead = null, bool requireCommittedPayload = false)
    {
        var journal = await ReadJournal(request.OperationId, ct);
        if (journal is not {State: "restored", FolderId.Length: > 0, Files.Count: > 0})
            throw new PikPakCliException("PIKPAK_RESTORE_REVIEW_REQUIRED");
        CheckIdentity(journal, request.Source, account);
        ValidateId(journal.FolderId);
        if (copies == null || copies.Identity != DownloadIdentity(request, output, journal))
            throw new PikPakCliException("PIKPAK_OPERATION_CHANGED");
        if (copies.Started == null || copies.Completed == null ||
            !copies.Started.IsSubsetOf(journal.Files.Select(file => file.Id)) ||
            !copies.Completed.Keys.ToHashSet(StringComparer.Ordinal).IsSubsetOf(journal.Files.Select(file => file.Path)) ||
            copies.RestoredHttpMembers?.Keys.Any(id => !copies.Started.Contains(id)) == true ||
            copies.RestoredHttpMembers is not {Count: > 0} &&
            !journal.Files.Any(file => copies.Started.Contains(file.Id) && !copies.Completed.ContainsKey(file.Path)))
            throw new PikPakCliException("PIKPAK_PARTIAL_DOWNLOAD_REVIEW_REQUIRED");
        var hasCommittedPayload = await VerifyPreparedRetryLocationAsync(request, output, journal, copies, verifyContent, ct, onVerificationRead);
        if (requireCommittedPayload && !hasCommittedPayload)
            throw new PikPakCliException("PIKPAK_PARTIAL_DOWNLOAD_REVIEW_REQUIRED");
        return journal;
    }

    private Task<DownloadJournal?> ReadDownloadJournal(string operationId, CancellationToken ct) =>
        PikPakCliStorage.ReadAsync<DownloadJournal>(Path.Combine(OperationPath(operationId), "download.json"), ct);

    private async Task<bool> VerifyPreparedRetryLocationAsync(CloudDownloadRequest request, string output, Journal journal,
        DownloadJournal copies, bool verifyContent, CancellationToken ct, Func<ValueTask>? onVerificationRead = null)
    {
        var planned = journal.Files.ToDictionary(file => PikPakCliStorage.Within(output, file.Path),
            ArtifactManifestFiles.PathComparer);
        var stage = DownloadStage(output, request.OperationId);
        if (journal.Files.Any(file => file.Path.Split('/')[0].Equals(Path.GetFileName(stage), StringComparison.OrdinalIgnoreCase)))
            throw new PikPakCliException("PIKPAK_LOCAL_PATH_INVALID");
        var restoredHttpRoot = Path.Combine(stage, RestoredHttpStage);
        var httpMembers = journal.Files.Where(file => copies.RestoredHttpMembers?.ContainsKey(file.Id) == true)
            .ToDictionary(file => RestoredHttpDirectory(stage, file), ArtifactManifestFiles.PathComparer);
        var verified = new HashSet<string>(ArtifactManifestFiles.PathComparer);
        var verifiedStaged = new HashSet<string>(ArtifactManifestFiles.PathComparer);
        var pending = new Stack<(string Path, int Depth)>();
        pending.Push((output, 0));
        var entries = 0;
        var hasCommittedPayload = false;
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
                if (ArtifactManifestFiles.PathComparer.Equals(directory.Path, restoredHttpRoot))
                {
                    if ((attributes & FileAttributes.Directory) == 0 || !httpMembers.TryGetValue(entry, out var httpMember))
                        throw new PikPakCliException("PIKPAK_PARTIAL_DOWNLOAD_REVIEW_REQUIRED");
                    var identity = RestoredHttpIdentity(request, journal, copies, httpMember);
                    if (_restoredOriginals == null || copies.RestoredHttpMembers![httpMember.Id] != identity.AttemptScope)
                        throw new PikPakCliException("PIKPAK_PARTIAL_DOWNLOAD_REVIEW_REQUIRED");
                    var payload = Path.Combine(entry, "payload");
                    var destination = PikPakCliStorage.Within(output, httpMember.Path);
                    var stagedReceipt = copies.Completed.GetValueOrDefault(httpMember.Path);
                    if (stagedReceipt?.RestoredHttp == true && File.Exists(destination) && !Path.Exists(payload))
                    {
                        await VerifyRecordedFileAsync(destination, httpMember, stagedReceipt, verifyContent, ct, onVerificationRead);
                        await _restoredOriginals.InspectHandedOffAsync(identity, entry, ct);
                        verified.Add(httpMember.Path);
                        continue;
                    }
                    var checkpoint = await _restoredOriginals.InspectAsync(identity, entry, ct);
                    hasCommittedPayload |= checkpoint.Exists && checkpoint.VerifiedBytes > 0;
                    if (checkpoint.IsComplete && stagedReceipt?.RestoredHttp == true)
                    {
                        await VerifyRecordedFileAsync(payload, httpMember, stagedReceipt, verifyContent, ct, onVerificationRead);
                        verifiedStaged.Add(httpMember.Path);
                    }
                    continue;
                }
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
                if (verified.Add(member.Path))
                    await VerifyRecordedFileAsync(entry, member, receipt, verifyContent, ct, onVerificationRead);
            }
        }
        verified.UnionWith(verifiedStaged);
        if (verified.Count != copies.Completed.Count) throw new PikPakCliException("PIKPAK_LOCAL_CHANGED");
        return hasCommittedPayload;
    }

    private static async Task VerifyRecordedFileAsync(string path, RemoteFile member, FileReceipt? receipt,
        bool verifyContent, CancellationToken ct, Func<ValueTask>? onVerificationRead)
    {
        PikPakCliStorage.NoLinks(path);
        ArtifactFileIdentity.RequireRegularFile(path);
        if (receipt == null || receipt.Size != member.Size || new FileInfo(path).Length != receipt.Size ||
            receipt.Sha256 is not {Length: 64} ||
            verifyContent && receipt.Sha256 != await PikPakCliStorage.FileHash(path, ct, onVerificationRead))
            throw new PikPakCliException("PIKPAK_LOCAL_CHANGED");
    }
}
