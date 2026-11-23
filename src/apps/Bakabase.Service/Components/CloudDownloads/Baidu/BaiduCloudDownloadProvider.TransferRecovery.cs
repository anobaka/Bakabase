using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Downloader.Cloud;

namespace Bakabase.Service.Components.CloudDownloads.Baidu;

public sealed partial class BaiduCloudDownloadProvider
{
    public bool IsTerminalTransferProblem(string problem) => BaiduTransferDiagnostics.IsTerminal(problem);

    public async Task<CloudTransferReceipt?> ReconcileTransferAsync(CloudTransferRequest request, CancellationToken ct)
    {
        var profile = await accounts.ReadInspectionProfileAsync(request.AccountId, ct);
        return (await ReadTransferJournalAsync(profile.Directory, request, ct))?.Receipt;
    }

    public async Task<CloudTransferRecoveryCheck> InspectTransferRecoveryAsync(CloudTransferRequest request,
        bool verifyRemote, CancellationToken ct)
    {
        try
        {
            var profile = await accounts.ReadInspectionProfileAsync(request.AccountId, ct);
            var journal = await ReadTransferJournalAsync(profile.Directory, request, ct);
            if (journal?.Receipt is {} receipt)
                return new(CloudTransferRecoveryDisposition.Reconcile, Receipt: receipt);
            if (journal?.Problem is {} problem)
                return new(CloudTransferRecoveryDisposition.Unavailable, problem);
            if (!verifyRemote) return new(CloudTransferRecoveryDisposition.Candidate);

            var result = await bridge.ExecuteAsync(profile.Directory, new
            {
                command = "inspect-transfer-target", expectedUid = profile.Account.Uid,
                request.OperationId, request.RemoteDirectory, request.Source.Url, request.Source.AccessCode
            }, null, ct);
            var latest = await accounts.ReadInspectionProfileAsync(request.AccountId, ct);
            if (result.GetProperty("uid").GetString() != profile.Account.Uid || latest.Account.Uid != profile.Account.Uid ||
                await ReadTransferJournalAsync(latest.Directory, request, ct) != journal)
                return new(CloudTransferRecoveryDisposition.Unavailable, "RECOVERY_TRANSFER_EVIDENCE_CHANGED");

            var state = result.GetProperty("state").GetString();
            if (state is "empty" or "absent" && journal?.Stage == "prepared")
                return new(CloudTransferRecoveryDisposition.Retry);
            if (state == "absent" || state == "empty" && journal?.Stage == "transferring")
                return new(CloudTransferRecoveryDisposition.RequiresConfirmation)
                {VerifiedEmptyTarget = state == "empty" && journal?.Stage == "transferring"};
            return new(CloudTransferRecoveryDisposition.Unavailable, "RECOVERY_TRANSFER_TARGET_UNVERIFIED");
        }
        catch (CloudProviderException error)
        {
            return new(CloudTransferRecoveryDisposition.Unavailable, error.Message);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or
                                             InvalidOperationException or JsonException or System.Collections.Generic.KeyNotFoundException)
        {
            return new(CloudTransferRecoveryDisposition.Unavailable, "RECOVERY_TRANSFER_TARGET_UNVERIFIED");
        }
    }

    private sealed record TransferJournal(string Stage, CloudTransferReceipt? Receipt, string? Problem = null);

    private static async Task<TransferJournal?> ReadTransferJournalAsync(string directory, CloudTransferRequest request,
        CancellationToken ct)
    {
        if (request.Source.ProviderId != "baidu" || request.Source.Kind != "share" || string.IsNullOrWhiteSpace(request.OperationId) ||
            !request.RemoteDirectory.StartsWith('/') || request.RemoteDirectory.Contains('\\') || request.RemoteDirectory.Contains('\0') ||
            request.RemoteDirectory.Split('/').Skip(1).Any(s => string.IsNullOrWhiteSpace(s) || s is "." or ".."))
            throw new CloudProviderException("BAIDU_OPERATION_MISMATCH", true);
        var key = TransferHash(request.OperationId);
        var expectedPath = request.RemoteDirectory + "/bakabase-" + key[..24];
        var fingerprint = TransferHash(request.Source.Url + "\0" + (request.Source.AccessCode ?? "") + "\0" + request.RemoteDirectory);
        var file = Path.Combine(directory, "operations", key + ".json");
        ArtifactManifestFiles.RejectLinks(file);
        JsonDocument parsed;
        try
        {
            await using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete,
                4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (input.Length > 32L * 1024 * 1024) throw new CloudProviderException("BAIDU_RECEIPT_READ_FAILED", true);
            parsed = await JsonDocument.ParseAsync(input, cancellationToken: ct);
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        using (parsed)
        {
            var saved = parsed.RootElement;
            if (saved.GetProperty("fingerprint").GetString() != fingerprint ||
                saved.GetProperty("remotePath").GetString() != expectedPath)
                throw new CloudProviderException("BAIDU_OPERATION_MISMATCH", true);
            var stage = saved.GetProperty("stage").GetString();
            if (saved.TryGetProperty("transferDiagnostic", out var diagnostic))
            {
                var code = diagnostic.ValueKind == JsonValueKind.Object && diagnostic.TryGetProperty("code", out var problem) &&
                    problem.ValueKind == JsonValueKind.String ? problem.GetString() : null;
                var details = BaiduTransferDiagnostics.Read(diagnostic, code);
                if (details == null || stage != (BaiduTransferDiagnostics.IsTerminal(code) ? "rejected" : "transferring"))
                    throw new CloudProviderException("BAIDU_RECEIPT_READ_FAILED", true);
                return new(stage, null, details.Code);
            }
            if (stage is "prepared" or "transferring") return new(stage, null);
            if (stage != "completed") throw new CloudProviderException("BAIDU_RECEIPT_READ_FAILED", true);
            var manifest = saved.GetProperty("manifest");
            if (manifest.ValueKind != JsonValueKind.Array || manifest.GetArrayLength() < 2 ||
                manifest[0].GetProperty("path").GetString() != expectedPath || !manifest[0].GetProperty("isDirectory").GetBoolean())
                throw new CloudProviderException("BAIDU_RECEIPT_READ_FAILED", true);
            return new(stage, ToReceipt(request.OperationId, request.AccountId, saved));
        }
    }

    private static string TransferHash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
