using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.BaiduPcsGo;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.CloudTools;
using Bakabase.Abstractions.Components.Network;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.InsideWorld.Business.Components.Artifacts;

namespace Bakabase.Service.Components.CloudDownloads.Baidu;

public sealed class BaiduPcsBridgeClient(BaiduPcsGoService dependency, BakabaseWebProxy proxy, ILogger<BaiduPcsBridgeClient>? logger = null)
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public async Task<JsonElement> ExecuteAsync(string configDirectory, object request,
        Func<JsonElement, Task>? progress, CancellationToken ct, bool observeStorageWrites = false)
    {
        await dependency.EnsureReadyAsync(ct);
        var info = new ProcessStartInfo(dependency.Executable)
        {
            UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true,
            RedirectStandardError = true, CreateNoWindow = true, WorkingDirectory = configDirectory
        };
        info.Environment["BAIDUPCS_GO_CONFIG_DIR"] = configDirectory;
        ConfigureProxy(info, proxy);
        using var process = Process.Start(info) ?? throw new CloudProviderException("BAIDU_BRIDGE_START_FAILED");
        using var cancel = ct.Register(() => { try { process.Kill(true); } catch { } });
        var exited = observeStorageWrites && FileIoObservationScope.Active ? process.WaitForExitAsync(CancellationToken.None) : null;
        var writeObservation = exited != null ? LocalFileProcessIo.ObserveStorageWritesAsync(process.Id, exited) : Task.CompletedTask;
        using var verificationReads = FileIoObservationScope.Active ? new VerificationReadObservation() : null;
        var stderr = process.StandardError.BaseStream.CopyToAsync(Stream.Null, CancellationToken.None);
        try
        {
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request, Json).AsMemory(), ct);
            process.StandardInput.Close();
            JsonElement? result = null;
            string? error = null;
            string? storageCode = null;
            CloudVerificationLocation? location = null;
            CloudTransferDiagnostic? transfer = null;
            var mayHaveSideEffects = true;
            while (await process.StandardOutput.ReadLineAsync(ct) is { } line)
            {
                using var parsed = JsonDocument.Parse(line);
                var value = parsed.RootElement;
                switch (value.GetProperty("type").GetString())
                {
                    case "progress":
                        verificationReads?.Record(value);
                        if (progress != null) await progress(value);
                        break;
                    case "result": result = value.Clone(); break;
                    case "error":
                        error = value.GetProperty("code").GetString();
                        storageCode = StorageCode(value);
                        location = VerificationLocation(value, error);
                        if (RecheckedManifestFile(value) is { } member)
                            logger?.LogWarning("Baidu verification failed for manifest file {FileNumber} with {Problem}", member, storageCode ?? error);
                        mayHaveSideEffects = !value.TryGetProperty("mayHaveSideEffects", out var effects) || effects.GetBoolean();
                        if (BaiduTransferDiagnostics.IsKnown(error))
                        {
                            transfer = value.TryGetProperty("transferDiagnostic", out var detail) ? BaiduTransferDiagnostics.Read(detail, error) : null;
                            if (transfer == null) error = "BAIDU_TRANSFER_RESPONSE_INVALID";
                            if (!BaiduTransferDiagnostics.IsTerminal(error)) mayHaveSideEffects = true;
                        }
                        break;
                }
            }
            await process.WaitForExitAsync(ct); await stderr;
            if (process.ExitCode != 0 || error != null || result == null)
            {
                if (storageCode != null) throw new LocalStorageException(storageCode);
                var code = error is { } safe && safe.StartsWith("BAIDU_", StringComparison.Ordinal) && safe.Length < 100
                    ? safe : "BAIDU_BRIDGE_FAILED";
                throw new CloudProviderException(code, mayHaveSideEffects) {VerificationLocation = location, TransferDiagnostic = transfer};
            }
            return result.Value;
        }
        finally
        {
            if (!process.HasExited) { try { process.Kill(true); } catch { } }
            await (exited ?? process.WaitForExitAsync(CancellationToken.None));
            await writeObservation;
            await stderr;
        }
    }

    internal sealed class VerificationReadObservation : IDisposable
    {
        private IFileIoObservation? _observation;
        private long? _total;
        private long _reported;

        public void Record(JsonElement progress)
        {
            if (!progress.TryGetProperty("stage", out var stage) || stage.ValueKind != JsonValueKind.String ||
                !stage.ValueEquals("verifying") ||
                !TryGetBytes(progress, "verifiedBytes", out var read) ||
                !TryGetBytes(progress, "verificationTotalBytes", out var total) || read < 0 || total < read)
            {
                EndVerification();
                return;
            }

            // An explicit zero starts each verification reader. Other progress values alone
            // cannot distinguish a new read from a resumed or partially observed operation.
            if (read == 0)
            {
                _reported = 0;
                _total = total;
                _observation ??= FileIoObservationScope.Begin(read: true, write: false);
                if (total == 0) EndVerification();
                return;
            }

            if (_total != total || read < _reported)
            {
                EndVerification();
                return;
            }

            if (read > _reported) _observation?.Add(read - _reported, 0);
            _reported = read;
            if (read == total) EndVerification();
        }

        private static bool TryGetBytes(JsonElement progress, string name, out long bytes)
        {
            bytes = 0;
            return progress.TryGetProperty(name, out var field) && field.ValueKind == JsonValueKind.Number &&
                field.TryGetInt64(out bytes);
        }

        public void Dispose() => EndVerification();

        private void EndVerification()
        {
            _observation?.Dispose();
            _observation = null;
            _total = null;
        }
    }

    internal static CloudVerificationLocation? VerificationLocation(JsonElement error, string? code)
    {
        if (code is not ("BAIDU_DOWNLOAD_CHECKSUM_MISMATCH" or "BAIDU_EXISTING_CHECKSUM_UNAVAILABLE" or
            "BAIDU_VERIFIED_FILE_MISSING" or "BAIDU_VERIFIED_FILE_REPLACED" or "BAIDU_VERIFIED_FILE_SIZE_CHANGED" or
            "BAIDU_VERIFIED_FILE_TIME_CHANGED" or "BAIDU_LOCAL_FILE_CHANGED" or "BAIDU_REMOTE_MANIFEST_CHANGED" or
            "BAIDU_OUTPUT_FILE_READ_FAILED")) return null;

        int Positive(string key) => error.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number &&
            value.TryGetInt32(out var number) && number > 0 ? number : 0;
        var count = Positive("manifestFileCount");
        var file = Positive("manifestFileNumber");
        if (count > 10_000 || file == 0 || file > count) return null;
        var member = new CloudVerificationLocation(file, count);
        if (code != "BAIDU_DOWNLOAD_CHECKSUM_MISMATCH") return member;
        var blocks = Positive("checksumBlockCount");
        var mismatches = Positive("mismatchedBlockCount");
        var first = Positive("firstMismatchedBlockNumber");
        if (first == 0 || first > blocks || mismatches == 0 || mismatches > blocks - first + 1 ||
            !error.TryGetProperty("firstMismatchedBlockBytes", out var length) || length.ValueKind != JsonValueKind.Number ||
            !length.TryGetInt64(out var bytes) || bytes < 0 || bytes == 0 && blocks != 1) return member;
        return member with {ChecksumMismatch = new(blocks, mismatches, first, bytes)};
    }

    // Keep the ordinal of the first failed check. File names and paths may contain private data.
    internal static int? RecheckedManifestFile(JsonElement error) =>
        error.TryGetProperty("manifestFileNumber", out var number) && number.ValueKind == JsonValueKind.Number &&
        number.TryGetInt32(out var member) && member is > 0 and <= 2_000_000 &&
        error.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.String &&
        code.GetString() is "BAIDU_VERIFIED_FILE_MISSING" or "BAIDU_VERIFIED_FILE_REPLACED" or
            "BAIDU_VERIFIED_FILE_SIZE_CHANGED" or "BAIDU_VERIFIED_FILE_TIME_CHANGED" or
            "BAIDU_OUTPUT_FILE_READ_FAILED" or "BAIDU_DOWNLOAD_CHECKSUM_MISMATCH" or "BAIDU_LOCAL_FILE_CHANGED"
            ? member : null;

    internal static string? StorageCode(JsonElement error) =>
        error.TryGetProperty("storage", out var storage) && storage.ValueKind == JsonValueKind.Object &&
        storage.TryGetProperty("kind", out var kind) && kind.ValueKind == JsonValueKind.String
            ? kind.GetString() switch
            {
                "diskFull" or "quota" => "LOCAL_STORAGE_FULL",
                "permission" => "LOCAL_STORAGE_ACCESS_DENIED",
                "unavailable" => "LOCAL_STORAGE_UNAVAILABLE",
                "unknown" when storage.TryGetProperty("operation", out var operation) && operation.ValueKind == JsonValueKind.String &&
                    operation.GetString() is "recheckVerifiedFile" or "inspectVerifiedHandle" => null,
                "unknown" => "LOCAL_STORAGE_WRITE_FAILED",
                _ => null
            } : null;

    internal static void ConfigureProxy(ProcessStartInfo info, BakabaseWebProxy proxy) =>
        CloudToolProxy.ApplyToEnvironment(info,
            proxy.ForDownloadTool(DownloadToolProxyTargets.BaiduPcsGo, ThirdPartyId.BaiduNetdisk), new Uri("https://pan.baidu.com"));
}
