using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Downloader.Cloud;

namespace Bakabase.Service.Components.CloudDownloads.WebProviders;

public abstract partial class WebCloudProvider
{
    private const string InspectReuseSourceMode = "inspectReuseSource";
    private const string InspectReuseDestinationMode = "inspectReuseDestination";
    private const string InspectionIncomplete = "DOWNLOAD_DESTINATION_INSPECTION_INCOMPLETE";
    // Load the published bridge without installing it in the stopped operation's state.
    // Stdin keeps both the script and request out of command-line length limits and logs.
    private const string BridgeStdinLoader = """
        import base64,sys
        source=base64.b64decode(sys.stdin.readline(),validate=False)
        exec(compile(source,'<bakabase-cloud-bridge>','exec'),{'__name__':'__main__'})
        """;

    private static readonly Lazy<string> EmbeddedBridgeInput = new(() =>
    {
        using var resource = typeof(WebCloudProvider).Assembly.GetManifestResourceStream("Bakabase.Service.CloudPythonBridge.py")
            ?? throw new InvalidOperationException("The cloud Python adapter resource is missing.");
        using var bytes = new MemoryStream();
        resource.CopyTo(bytes);
        return Convert.ToBase64String(bytes.ToArray());
    });

    private BridgeReuseSource? DescribeReuseSource(CloudDownloadRequest request, string root) =>
        request.ReuseSource is { } source
            ? new(source.OutputDirectory, StateDirectory(root, ProviderId, source.OperationId),
                source.InventoryFingerprint, source.DisableSourceRead)
            : null;

    protected async Task<CloudDownloadReuseInventory> InspectBridgeReuseSourceAsync(CloudDownloadRequest request,
        CancellationToken ct)
    {
        var (output, exitCode) = await RunBridgeInspectionAsync(request, InspectReuseSourceMode, ct);
        return ReadReuseInventory(output, exitCode);
    }

    protected async Task<long?> InspectBridgeReuseDestinationAsync(CloudDownloadRequest request, CancellationToken ct)
    {
        var (output, exitCode) = await RunBridgeInspectionAsync(request, InspectReuseDestinationMode, ct);
        return ReadReuseWriteBudget(output, exitCode);
    }

    private async Task<(string Output, int ExitCode)> RunBridgeInspectionAsync(CloudDownloadRequest request, string mode,
        CancellationToken ct)
    {
        var gate = _operations.GetOrAdd(OperationKey(request.OperationId), _ => new SemaphoreSlim(1, 1));
        if (!await gate.WaitAsync(0, ct)) throw new CloudProviderException("DOWNLOAD_DESTINATION_ACTIVE");
        try
        {
            ValidateShareUrl(ProviderId, request.Source.Url);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(90));
            var token = timeout.Token;
            var root = appData();
            var state = StateDirectory(root, ProviderId, request.LayoutOperationId);
            var start = tool.CaptureExecution().StartInfo(["-I", "-u", "-c", BridgeStdinLoader], root);
            using var process = new Process {StartInfo = start};
            process.Start();
            try
            {
                var errors = process.StandardError.BaseStream.CopyToAsync(Stream.Null, token);
                var output = process.StandardOutput.ReadToEndAsync(token);
                await process.StandardInput.WriteLineAsync(EmbeddedBridgeInput.Value.AsMemory(), token);
                await process.StandardInput.WriteAsync(SerializeRequest(ProviderId, request, state, null,
                    mode: mode, reuseSource: mode == InspectReuseDestinationMode ? DescribeReuseSource(request, root) : null)
                    .AsMemory(), token);
                process.StandardInput.Close();
                await process.WaitForExitAsync(token);
                await errors;
                return (await output, process.ExitCode);
            }
            finally
            {
                if (!process.HasExited)
                {
                    try {process.Kill(entireProcessTree: true);}
                    catch (InvalidOperationException) { }
                    await process.WaitForExitAsync(CancellationToken.None);
                }
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {throw new CloudProviderException(InspectionIncomplete);}
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or JsonException or Win32Exception)
        {throw new CloudProviderException(InspectionIncomplete);}
        finally {gate.Release();}
    }

    internal static CloudDownloadReuseInventory ReadReuseInventory(string output, int exitCode)
    {
        try
        {
            using var document = JsonDocument.Parse(output);
            var response = InspectionResult(document.RootElement, exitCode, "reuseInventory");
            var estimate = response.Deserialize<CloudDownloadReuseEstimate>(JsonSerializerOptions.Web);
            var fingerprint = response.GetProperty("inventoryFingerprint").GetString();
            if (estimate is not {FileCount: > 0 and <= 100000, TotalBytes: > 0, CandidateFileCount: > 0, CandidateBytes: > 0} ||
                estimate.CandidateFileCount > estimate.FileCount || estimate.CandidateBytes > estimate.TotalBytes ||
                fingerprint is not {Length: 64} || !fingerprint.All(Uri.IsHexDigit))
                throw new CloudProviderException(InspectionIncomplete);
            return new(estimate, fingerprint);
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException and not CloudProviderException)
        {throw new CloudProviderException(InspectionIncomplete);}
    }

    internal static long? ReadReuseWriteBudget(string output, int exitCode)
    {
        try
        {
            using var document = JsonDocument.Parse(output);
            var budget = InspectionResult(document.RootElement, exitCode, "reuseWriteBudget").GetProperty("maximumAdditionalBytes");
            if (budget.ValueKind == JsonValueKind.Null) return null;
            if (budget.ValueKind != JsonValueKind.Number || !budget.TryGetInt64(out var bytes) || bytes < 0)
                throw new CloudProviderException(InspectionIncomplete);
            return bytes;
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException and not CloudProviderException)
        {throw new CloudProviderException(InspectionIncomplete);}
    }

    private static JsonElement InspectionResult(JsonElement response, int exitCode, string expectedType)
    {
        if (response.ValueKind != JsonValueKind.Object ||
            !response.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String)
            throw new CloudProviderException(InspectionIncomplete);
        if (type.GetString() == "error" && response.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String)
        {
            // Never propagate provider output, absolute state paths or locator text into a preview.
            var reason = message.GetString()?.Split(':', 2)[0];
            if (reason is "DOWNLOAD_DESTINATION_PROVIDER_STATE_UNKNOWN" or "DOWNLOAD_DESTINATION_PROVIDER_STATE_PRESENT" or
                "DOWNLOAD_DESTINATION_REUSE_UNSUPPORTED" or "DOWNLOAD_DESTINATION_REUSE_SOURCE_CHANGED" or
                "DOWNLOAD_DESTINATION_INSPECTION_INCOMPLETE")
                throw new CloudProviderException(reason);
            throw new CloudProviderException(InspectionIncomplete);
        }
        if (exitCode != 0 || type.GetString() != expectedType)
            throw new CloudProviderException(InspectionIncomplete);
        return response;
    }
}
