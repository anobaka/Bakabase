using System;
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

public sealed class BaiduCloudDownloadProvider(BaiduPcsAccountStore accounts, BaiduPcsBridgeClient bridge,
    BaiduPcsGoService dependency) : ICloudDownloadProvider
{
    public string ProviderId => "baidu";
    public bool SupportsTransfer => true;

    public async Task<CloudReadiness> CheckReadyAsync(string? accountId, CancellationToken ct)
    {
        try { await dependency.EnsureReadyAsync(ct); }
        catch (Exception e) when (e is not OperationCanceledException) { return new(false, e.Message); }
        var id = await accounts.ResolveAccountIdAsync(accountId, ct);
        var account = (await accounts.ListAsync(ct)).FirstOrDefault(a => a.Id == id);
        return account?.IsLoggedIn == true ? new(true) : new(false, "BAIDU_LOGIN_REQUIRED");
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
            receipt.GetProperty("manifest").GetRawText()));

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
        return await accounts.WithAccountAsync(pinned.AccountId, async (directory, account) =>
        {
            if (!account.IsLoggedIn) throw new CloudProviderException("BAIDU_LOGIN_REQUIRED");
            var result = await bridge.ExecuteAsync(directory, new
            {
                command = "download", request.OperationId, remotePath = pinned.Path, request.OutputDirectory,
                expectedManifest = pinned.ManifestJson == null ? (JsonElement?)null : JsonSerializer.Deserialize<JsonElement>(pinned.ManifestJson)
            }, async update =>
            {
                var completed = update.GetProperty("completedBytes").GetInt64();
                var total = update.GetProperty("totalBytes").GetInt64();
                await progress(new CloudDownloadProgress(total == 0 ? 100 : Math.Min(100, completed * 100d / total),
                    completed, total, "Downloading"));
            }, ct);
            return new CloudDownloadResult(result.GetProperty("files").EnumerateArray().Select(f => f.GetString()!).ToArray());
        }, ct);
    }
}
