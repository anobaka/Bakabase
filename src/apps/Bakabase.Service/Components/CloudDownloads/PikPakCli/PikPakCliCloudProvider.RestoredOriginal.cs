using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Modules.Downloader.Models;
using Bakabase.Service.Components.CloudDownloads.WebProviders;

namespace Bakabase.Service.Components.CloudDownloads.PikPakCli;

public sealed partial class PikPakCliCloudProvider
{
    private const string RestoredHttpStage = "restored-http-v1";

    private static string DownloadStage(string output, string operationId) =>
        Path.Combine(output, ".bakabase-pikpak-" + PikPakCliStorage.Hash(operationId));

    private static string NativePayload(string stage, RemoteFile file) =>
        Path.Combine(stage, PikPakCliStorage.Hash(file.Id) + ".payload");

    // The CLI may preallocate the entire original. Keep its private .part throughout HTTP recovery;
    // only the separate HTTP checkpoint can authorize resuming or publishing bytes.
    private static string InspectNativePart(string path, RemoteFile file, out string? version)
    {
        PikPakCliStorage.NoLinks(path);
        using var part = RegularFileHandle.TryOpenRead(path);
        if (part == null || part.Length > file.Size || !RegularFileHandle.HasSingleLink(part.SafeFileHandle))
            throw new PikPakCliException("PIKPAK_PARTIAL_DOWNLOAD_REVIEW_REQUIRED");
        var identity = RegularFileHandle.Identity(part, path);
        // Lack of a reliable generation need not prevent the HTTP retry. It prevents retiring this file.
        try { version = RegularFileHandle.ReadRetirementVersion(part, path); }
        catch (IOException) { version = null; }
        return identity;
    }

    private static string RestoredHttpDirectory(string stage, RemoteFile file) =>
        Path.Combine(stage, RestoredHttpStage, PikPakCliStorage.Hash(file.Id));

    private static AuthorizedOriginalIdentity RestoredHttpIdentity(CloudDownloadRequest request, Journal journal,
        DownloadJournal copies, RemoteFile file) =>
        new("pikpak", request.OperationId, PikPakCliStorage.Hash(journal.AccountId + "\0" + journal.AccountVersion),
            journal.FolderId!, file.Id, PikPakCliStorage.Hash(JsonSerializer.Serialize(file, PikPakCliStorage.Json)),
            file.Size, PikPakCliStorage.Hash(copies.Identity + "\0" + RestoredHttpStage));

    private async Task<VerifiedOriginalPayload> VerifySealedOriginalAsync(CloudDownloadRequest request, Journal journal,
        DownloadJournal copies, RemoteFile file, string directory, string expectedPayload, Func<ValueTask> onRead, CancellationToken ct)
    {
        var identity = RestoredHttpIdentity(request, journal, copies, file);
        if (_restoredOriginals == null || copies.RestoredHttpMembers is not {} members ||
            !members.TryGetValue(file.Id, out var scope) || scope != identity.AttemptScope)
            throw new PikPakCliException("PIKPAK_PARTIAL_DOWNLOAD_REVIEW_REQUIRED");
        var verified = await _restoredOriginals.VerifyCompletedAsync(identity, directory, ct, onRead);
        if (verified.Length != file.Size || !ArtifactManifestFiles.PathComparer.Equals(
                Path.GetFullPath(verified.Path), expectedPayload))
            throw new PikPakCliException("PIKPAK_LOCAL_CHANGED");
        return verified;
    }

    private async Task DownloadRestoredOriginalAsync(CloudDownloadRequest request, Journal journal,
        DownloadJournal copies, RemoteFile file, PikPakCliAccountLease account, string directory, string part,
        long completed, long total, Func<CloudDownloadProgress, Task> progress, CancellationToken ct)
    {
        if (_restoredOriginals == null) throw new PikPakCliException("PIKPAK_ORIGINAL_FILE_UNAVAILABLE");
        var identity = RestoredHttpIdentity(request, journal, copies, file);
        copies.RestoredHttpMembers ??= new(StringComparer.Ordinal);
        if (!copies.RestoredHttpMembers.TryGetValue(file.Id, out var boundScope))
        {
            PikPakCliStorage.NoLinks(directory);
            if (Path.Exists(directory)) throw new PikPakCliException("PIKPAK_PARTIAL_DOWNLOAD_REVIEW_REQUIRED");
            var nativePart = NativePayload(DownloadStage(request.OutputDirectory, request.OperationId), file) + ".part";
            if (Path.Exists(nativePart))
            {
                if (!copies.Started.Contains(file.Id) || copies.Completed.ContainsKey(file.Path))
                    throw new PikPakCliException("PIKPAK_PARTIAL_DOWNLOAD_REVIEW_REQUIRED");
                copies.PreservedNativePartIdentities ??= new(StringComparer.Ordinal);
                copies.PreservedNativePartIdentities.Add(file.Id, InspectNativePart(nativePart, file, out var version));
                if (version != null)
                {
                    copies.PreservedNativePartVersions ??= new(StringComparer.Ordinal);
                    copies.PreservedNativePartVersions.Add(file.Id, version);
                }
            }
            copies.RestoredHttpMembers.Add(file.Id, identity.AttemptScope);
        }
        else if (boundScope != identity.AttemptScope)
            throw new PikPakCliException("PIKPAK_PARTIAL_DOWNLOAD_REVIEW_REQUIRED");

        // Bind an individual path to the frozen provider receipt before HTTP can create files.
        copies.Started.Add(file.Id);
        await PikPakCliStorage.WriteAsync(Path.Combine(OperationPath(request.OperationId), "download.json"), copies, ct);
        await progress(new(null, completed, total, "Downloading PikPak original files"));
        string? resolverProblem = null;
        async Task<Uri> Resolve(CancellationToken token)
        {
            resolverProblem = null;
            try { return await ResolveRestoredOriginalUrlAsync(journal, file, account, token); }
            catch (PikPakCliException error)
            {
                resolverProblem = error.Code;
                throw new AuthorizedOriginalDownloadException(error.Code == "PIKPAK_REMOTE_CHANGED"
                    ? "ORIGINAL_HTTP_SOURCE_CHANGED" : "ORIGINAL_HTTP_LOCATION_UNAVAILABLE");
            }
        }
        string downloaded;
        try
        {
            downloaded = await _restoredOriginals.DownloadAsync(identity, directory,
                PublicShareDownloadClients.PikPakOfficialOriginal, Resolve, update =>
                {
                    var received = completed + Math.Clamp(update.CompletedBytes, 0, file.Size);
                    return progress(new(total == 0 ? null : 100d * received / total,
                        received, total, update.Stage ?? "Downloading PikPak original files"));
                }, ct);
        }
        catch (AuthorizedOriginalDownloadException error) when (resolverProblem != null &&
            (error.Code is "ORIGINAL_HTTP_SOURCE_CHANGED" or "ORIGINAL_HTTP_LOCATION_UNAVAILABLE"))
        {
            // Only the provider's own machine code crosses this boundary. Never expose
            // the resolver exception, CLI response, or an address containing a signature.
            throw new PikPakCliException(resolverProblem);
        }
        if (!ArtifactManifestFiles.PathComparer.Equals(Path.GetFullPath(downloaded), part))
            throw new PikPakCliException("PIKPAK_PARTIAL_DOWNLOAD_REVIEW_REQUIRED");
    }

    private static PikPakCliException RestoredHttpProblem(AuthorizedOriginalDownloadException error) => new(error.Code switch
    {
        "ORIGINAL_HTTP_TRANSFER_FAILED" or "ORIGINAL_HTTP_LOCATION_UNAVAILABLE" => "PIKPAK_NETWORK_UNAVAILABLE",
        "ORIGINAL_HTTP_RATE_LIMITED" => "CLOUD_RATE_LIMITED",
        "ORIGINAL_HTTP_SOURCE_CHANGED" => "PIKPAK_REMOTE_CHANGED",
        "ORIGINAL_HTTP_CHECKPOINT_INVALID" or "ORIGINAL_HTTP_BUSY" => "PIKPAK_PARTIAL_DOWNLOAD_REVIEW_REQUIRED",
        _ => error.Code
    });

    private static async Task<Uri> ResolveRestoredOriginalUrlAsync(Journal journal, RemoteFile file,
        PikPakCliAccountLease account, CancellationToken ct)
    {
        if (journal.State != "restored" || journal.FolderId == null ||
            journal.AccountId != account.Id || journal.AccountVersion != account.Version ||
            string.IsNullOrWhiteSpace(file.Hash) || string.IsNullOrWhiteSpace(file.Version))
            throw new PikPakCliException("PIKPAK_REMOTE_CHANGED");

        await account.RequireOnline(ct);
        // The file's exact parent chain remains relative to the recorded restored folder ID.
        // A file found via get alone could have been moved out of that copy or replaced.
        var parent = journal.FolderId;
        var segments = file.Path.Split('/');
        foreach (var segment in segments.Take(segments.Length - 1))
        {
            var directory = (await ListAccount(account, parent, ct))
                .SingleOrDefault(entry => entry.IsFolder && entry.Name == segment);
            parent = directory?.Id ?? throw new PikPakCliException("PIKPAK_REMOTE_CHANGED");
        }
        var listed = (await ListAccount(account, parent, ct)).SingleOrDefault(entry => entry.Id == file.Id);
        if (listed == null || listed.IsFolder || listed.Name != segments[^1] ||
            listed.Size != file.Size || listed.Hash != file.Hash || listed.Version != file.Version)
            throw new PikPakCliException("PIKPAK_REMOTE_CHANGED");

        var details = await account.Run(["get", file.Id, "--url-type", "download"], ct);
        return ReadRestoredOriginalUrl(details, file, listed.Name);
    }

    internal static Uri ReadRestoredOriginalUrl(JsonElement details, RemoteFile file, string name)
    {
        var exactSize = PikPakCliJson.Number(details, "size");
        if (PikPakCliJson.Text(details, "id") != file.Id || PikPakCliJson.Text(details, "type") != "file" ||
            PikPakCliJson.Text(details, "name") != name || PikPakCliJson.Text(details, "hash") != file.Hash ||
            (exactSize.HasValue ? exactSize != file.Size : !DisplaySizeMatches(PikPakCliJson.Text(details, "size"), file.Size)))
            throw new PikPakCliException("PIKPAK_REMOTE_CHANGED");
        // The official get command has separate original and playback URL fields. Never
        // fall back to playback, and never persist or forward its response as a task record.
        var url = PikPakCliJson.Text(details, "download_url");
        if (url is not {Length: > 0 and <= 8192} || !Uri.TryCreate(url, UriKind.Absolute, out var original) ||
            original.Fragment.Length > 0 || !PublicShareDownloadClients.IsProviderHost("pikpak", original))
            throw new PikPakCliException("PIKPAK_ORIGINAL_FILE_UNAVAILABLE");
        return original;
    }
}
