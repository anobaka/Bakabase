using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.BaiduPcsGo;
using Bakabase.Modules.Downloader.Cloud;

namespace Bakabase.Service.Components.CloudDownloads.Baidu;

public sealed partial class BaiduCloudDownloadProvider(BaiduPcsAccountStore accounts, BaiduPcsBridgeClient bridge,
    BaiduPcsGoService dependency) : ICloudDownloadProvider, ICloudRetainedDownloadRecoveryProvider,
    ICloudExistingDownloadVerificationProvider, ICloudDownloadDestinationRevisionProvider, ICloudDownloadWriteBudgetProvider
{
    private static readonly BaiduDownloadDestinationLease DownloadDestinations = new();
    public string ProviderId => "baidu";
    public bool SupportsTransfer => true;
    public bool CanCorrectAccessCodeAfterFailure(string code) =>
        code is "BAIDU_ACCESS_CODE_REQUIRED" or "BAIDU_SHARE_ACCESS_CODE_REJECTED";

    public bool CanVerifyExistingDownloadFailure(string code) => code is
        "BAIDU_OUTPUT_FILE_READ_FAILED" or "BAIDU_LOCAL_FILE_CHANGED" or "BAIDU_DOWNLOAD_CHECKSUM_MISMATCH";

    public bool CanRevalidateRetainedDownloadFailure(string code) => code is
        "LOCAL_STORAGE_FULL" or "LOCAL_STORAGE_WRITE_FAILED" or "LOCAL_STORAGE_ACCESS_DENIED" or "LOCAL_STORAGE_UNAVAILABLE" or "LOCAL_STORAGE_RESERVE_LOW" or
        "BAIDU_OUTPUT_DIRECTORY_FAILED" or "BAIDU_OUTPUT_FILE_READ_FAILED" or "BAIDU_DOWNLOAD_FAILED" or
        "BAIDU_OUTPUT_FILE_INVALID" or "BAIDU_LOCAL_PATH_TOO_LONG";

    public async Task<CloudReadiness> InspectRetainedDownloadAsync(CloudDownloadRequest request, bool verifyRemote,
        CancellationToken ct)
    {
        var entry = request.RemoteEntry;
        if (entry?.ProviderId != ProviderId || request.AccountId != entry.AccountId ||
            string.IsNullOrWhiteSpace(entry.ManifestJson) || !entry.Path.StartsWith('/'))
            return new(false, "RECOVERY_DOWNLOAD_IDENTITY_CHANGED");
        try
        {
            var profile = await accounts.ReadInspectionProfileAsync(entry.AccountId, ct);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(45));
            var result = await bridge.ExecuteAsync(profile.Directory, new
            {
                command = "inspect-download", expectedUid = profile.Account.Uid, request.OperationId,
                remotePath = entry.Path, request.OutputDirectory, verifyRemote,
                expectedManifest = JsonSerializer.Deserialize<JsonElement>(entry.ManifestJson)
            }, null, timeout.Token);
            var latest = await accounts.ReadInspectionProfileAsync(entry.AccountId, ct);
            if (latest.Account.Uid != profile.Account.Uid || result.GetProperty("uid").GetString() != profile.Account.Uid)
                return new(false, "BAIDU_ACCOUNT_IDENTITY_CHANGED");
            return result.TryGetProperty("canResume", out var allowed) && allowed.ValueKind == JsonValueKind.True
                ? new(true) : new(false, "RECOVERY_DOWNLOAD_REVIEW_REQUIRED");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { return new(false, "RECOVERY_CHECK_INCOMPLETE"); }
        catch (Exception error) when (error is InvalidOperationException or IOException or ArgumentException or JsonException)
        { return new(false, error.Message); }
    }

    public async Task<CloudReadiness> InspectExistingDownloadAsync(CloudDownloadRequest request, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(45));
        try { _ = await VerifyExistingCoreAsync(request, false, timeout.Token); return new(true); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { return new(false, "RECOVERY_CHECK_INCOMPLETE"); }
        catch (Exception error) when (error is InvalidOperationException or IOException or ArgumentException or JsonException)
        { return new(false, error.Message); }
    }

    public async Task<CloudExistingDownloadVerification> VerifyExistingDownloadAsync(
        CloudDownloadRequest request, CancellationToken ct)
    {
        var files = await VerifyExistingCoreAsync(request, true, ct);
        var profile = await accounts.ReadInspectionProfileAsync(request.RemoteEntry!.AccountId, ct);
        var root = await BaiduOwnedDownloadRoot.ResolveAsync(profile.Directory, request.RemoteEntry, request.OutputDirectory, ct);
        return new(files, CloudDownloadPayloadRoot.Select(request.OutputDirectory, root, files.Select(f => f.Path).ToArray()));
    }

    private async Task<System.Collections.Generic.IReadOnlyList<CloudVerifiedExistingFile>> VerifyExistingCoreAsync(
        CloudDownloadRequest request, bool verifyContent, CancellationToken ct)
    {
        var entry = request.RemoteEntry;
        if (entry?.ProviderId != ProviderId || request.AccountId != entry.AccountId || string.IsNullOrWhiteSpace(entry.ManifestJson))
            throw new CloudProviderException("RECOVERY_DOWNLOAD_IDENTITY_CHANGED");
        var profile = await accounts.ReadInspectionProfileAsync(entry.AccountId, ct);
        var result = await bridge.ExecuteAsync(profile.Directory, new
        {
            command = "verify-existing", expectedUid = profile.Account.Uid, request.OperationId,
            remotePath = entry.Path, request.OutputDirectory, verifyRemote = verifyContent,
            expectedManifest = JsonSerializer.Deserialize<JsonElement>(entry.ManifestJson)
        }, null, ct);
        var latest = await accounts.ReadInspectionProfileAsync(entry.AccountId, ct);
        if (latest.Account.Uid != profile.Account.Uid || result.GetProperty("uid").GetString() != profile.Account.Uid)
            throw new CloudProviderException("BAIDU_ACCOUNT_IDENTITY_CHANGED");
        var files = JsonSerializer.Deserialize<CloudVerifiedExistingFile[]>(result.GetProperty("files"), JsonSerializerOptions.Web);
        if (files is not {Length: > 0} || verifyContent && files.Any(f => f.Length < 0 || f.Sha256?.Length != 64))
            throw new CloudProviderException("BAIDU_EXISTING_DOWNLOAD_UNVERIFIED");
        return files;
    }

    public async Task<CloudReadiness> CheckReadyAsync(string? accountId, CancellationToken ct)
    {
        try { await dependency.EnsureReadyAsync(ct); }
        catch (Exception e) when (e is not OperationCanceledException) { return new(false, e.Message); }
        var id = await accounts.ResolveAccountIdAsync(accountId, ct);
        var account = (await accounts.ListAsync(ct)).FirstOrDefault(a => a.Id == id);
        if (account?.IsLoggedIn != true) return new(false, "BAIDU_LOGIN_REQUIRED");
        // Admission uses a synchronous snapshot; refresh once per TTL during async preflight,
        // never during UI polling or while holding a scheduler admission lock.
        await accounts.RefreshMembershipAsync(account.Id, ct, false);
        return new(true);
    }

    public async Task<CloudTransferReceipt> TransferAsync(CloudTransferRequest request, CancellationToken ct)
    {
        var id = await accounts.ResolveAccountIdAsync(request.AccountId, ct) ?? throw new CloudProviderException("BAIDU_LOGIN_REQUIRED");
        return await accounts.WithAccountAsync(id, async (directory, account) =>
        {
            if (!account.IsLoggedIn) throw new CloudProviderException("BAIDU_LOGIN_REQUIRED");
            var result = await bridge.ExecuteAsync(directory, new
            {
                command = "transfer", request.OperationId, request.Source.Url, request.Source.AccessCode,
                request.RemoteDirectory
            }, null, ct);
            return ToReceipt(request.OperationId, id, result.GetProperty("receipt"));
        }, ct);
    }

    public async Task<CloudTransferReceipt?> ReconcileTransferAsync(CloudTransferRequest request, CancellationToken ct)
    {
        var id = await accounts.ResolveAccountIdAsync(request.AccountId, ct);
        if (id == null) return null;
        return await accounts.WithAccountAsync<CloudTransferReceipt?>(id, async (directory, _) =>
        {
            var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.OperationId))).ToLowerInvariant();
            var journal = Path.Combine(directory, "operations", key + ".json");
            if (!File.Exists(journal)) return null;
            using var parsed = JsonDocument.Parse(await File.ReadAllTextAsync(journal, ct));
            var receipt = parsed.RootElement;
            var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                request.Source.Url + "\0" + (request.Source.AccessCode ?? "") + "\0" + request.RemoteDirectory))).ToLowerInvariant();
            if (receipt.GetProperty("fingerprint").GetString() != fingerprint)
                throw new CloudProviderException("BAIDU_OPERATION_MISMATCH", true);
            return receipt.GetProperty("stage").GetString() == "completed" ? ToReceipt(request.OperationId, id, receipt) : null;
        }, ct);
    }

    private static CloudTransferReceipt ToReceipt(string operationId, string accountId, JsonElement receipt) =>
        new(operationId, new CloudRemoteEntry("baidu", accountId, receipt.GetProperty("remotePath").GetString()!,
            receipt.GetProperty("manifest").GetRawText())
        {
            OwnedContainer = new(operationId, receipt.GetProperty("remotePath").GetString()!)
        });

    internal static CloudDownloadProgress ParseProgress(JsonElement update)
    {
        // Checksum reads are activity, but must never become downloaded bytes or 100% completion.
        if (update.TryGetProperty("stage", out var stage) && stage.GetString() == "verifying")
            return new(null, null, null, "Verifying");
        var completed = update.GetProperty("completedBytes").GetInt64();
        var total = update.GetProperty("totalBytes").GetInt64();
        return new(total == 0 ? 100 : Math.Min(100, completed * 100d / total), completed, total, "Downloading");
    }

    public async Task<CloudDownloadResult> DownloadAsync(CloudDownloadRequest request,
        Func<CloudDownloadProgress, Task> progress, CancellationToken ct)
    {
        var entry = request.RemoteEntry;
        if (entry == null && request.Source.Kind == "remote" && request.Source.Url.StartsWith('/'))
        {
            var id = await accounts.ResolveAccountIdAsync(request.AccountId, ct) ?? throw new CloudProviderException("BAIDU_LOGIN_REQUIRED");
            entry = new CloudRemoteEntry(ProviderId, id, request.Source.Url);
        }
        if (entry == null) throw new CloudProviderException("BAIDU_TRANSFER_REQUIRED");
        if (entry.ProviderId != ProviderId || request.AccountId != null && request.AccountId != entry.AccountId)
            throw new CloudProviderException("BAIDU_ACCOUNT_MISMATCH");
        var pinned = entry;
        using var destinationLease = await DownloadDestinations.AcquireAsync(request.OutputDirectory, ct);
        return await accounts.WithDownloadAccountAsync(pinned.AccountId, async (directory, account) =>
        {
            if (!account.IsLoggedIn) throw new CloudProviderException("BAIDU_LOGIN_REQUIRED");
            var result = await bridge.ExecuteAsync(directory, new
            {
                command = "download", request.OperationId, remotePath = pinned.Path, request.OutputDirectory,
                expectedUid = account.Uid, connectionsPerFile = account.EffectiveConnectionsPerFile,
                expectedManifest = pinned.ManifestJson == null ? (JsonElement?)null : JsonSerializer.Deserialize<JsonElement>(pinned.ManifestJson)
            }, async update =>
            {
                await progress(ParseProgress(update));
            }, ct);
            return new CloudDownloadResult(result.GetProperty("files").EnumerateArray().Select(f => f.GetString()!).ToArray())
            {
                PayloadRootDirectory = await BaiduOwnedDownloadRoot.ResolveAsync(directory, pinned, request.OutputDirectory, ct)
            };
        }, ct);
    }

    public async Task<CloudDownloadWritePlan?> PrepareWriteAsync(CloudDownloadRequest request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (request.RemoteEntry?.ManifestJson == null)
        {
            var entry = request.RemoteEntry;
            if (entry == null && request.Source.ProviderId == ProviderId && request.Source.Kind == "remote" && request.Source.Url.StartsWith('/'))
            {
                var id = await accounts.ResolveAccountIdAsync(request.AccountId, ct) ?? throw new CloudProviderException("BAIDU_LOGIN_REQUIRED");
                entry = new(ProviderId, id, request.Source.Url);
            }
            if (entry == null || entry.ProviderId != ProviderId || request.AccountId != null && entry.AccountId != request.AccountId)
                throw new CloudProviderException("BAIDU_DOWNLOAD_IDENTITY_INVALID");
            var pinned = entry;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(45));
            var manifest = await accounts.WithReadAccountAsync(pinned.AccountId, async (directory, account) =>
            {
                var result = await bridge.ExecuteAsync(directory, new { command = "manifest", expectedUid = account.Uid, remotePath = pinned.Path }, null, timeout.Token);
                if (result.GetProperty("uid").GetString() != account.Uid) throw new CloudProviderException("BAIDU_ACCOUNT_IDENTITY_CHANGED");
                return result.GetProperty("manifest").GetRawText();
            }, timeout.Token);
            request = request with { AccountId = pinned.AccountId, RemoteEntry = pinned with { ManifestJson = manifest } };
        }
        var preparedAccount = request.RemoteEntry?.AccountId;
        if (string.IsNullOrWhiteSpace(preparedAccount) || request.AccountId != null && request.AccountId != preparedAccount)
            throw new CloudProviderException("BAIDU_DOWNLOAD_IDENTITY_INVALID");
        // Legacy requests can pin the account exclusively in RemoteEntry. Carry that
        // same identity into every prepared request; never resolve a new default here.
        request = request with { AccountId = preparedAccount };
        var budget = EstimateWriteBudget(request) ?? throw new CloudProviderException("BAIDU_DOWNLOAD_IDENTITY_INVALID");
        return new(request, budget);
    }

    internal static IReadOnlyList<CloudDownloadWriteBudget>? EstimateWriteBudget(CloudDownloadRequest request)
    {
        if (request.RemoteEntry?.ManifestJson == null) return null;
        if (request.RemoteEntry.ProviderId != "baidu" || request.AccountId != null && request.AccountId != request.RemoteEntry.AccountId ||
            !Path.IsPathFullyQualified(request.OutputDirectory)) throw new CloudProviderException("BAIDU_DOWNLOAD_IDENTITY_INVALID");
        try
        {
            using var document = JsonDocument.Parse(request.RemoteEntry.ManifestJson);
            var manifest = document.RootElement;
            if (manifest.ValueKind != JsonValueKind.Array || manifest.GetArrayLength() is 0 or > 10000)
                throw new CloudProviderException("BAIDU_DOWNLOAD_IDENTITY_INVALID");
            var ids = new HashSet<long>();
            var paths = new HashSet<string>(StringComparer.Ordinal);
            long bytes = 0;
            foreach (var entry in manifest.EnumerateArray())
            {
                var id = entry.GetProperty("id").GetInt64();
                var path = entry.GetProperty("path").GetString();
                var size = entry.GetProperty("size").GetInt64();
                var isDirectory = entry.GetProperty("isDirectory").GetBoolean();
                if (id <= 0 || size < 0 || path == null || !path.StartsWith('/') || !ids.Add(id) || !paths.Add(path))
                    throw new CloudProviderException("BAIDU_DOWNLOAD_IDENTITY_INVALID");
                if (!isDirectory) bytes = checked(bytes + size);
            }
            // Full payload, not file length/progress subtraction: sparse/partial local bytes are not trusted.
            return [new(request.OutputDirectory, bytes)];
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or OverflowException)
        { throw new CloudProviderException("BAIDU_DOWNLOAD_IDENTITY_INVALID"); }
    }
}
