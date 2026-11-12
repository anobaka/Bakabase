using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Abstractions.Components.Network;
using Bakabase.InsideWorld.Business.Components.Dependency.Exceptions;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.CloudTools;
using Bakabase.Modules.Downloader.Cloud;

namespace Bakabase.Service.Components.CloudDownloads.WebProviders;

public sealed class GoogleDriveCloudProvider(GdownService tool, AppService appService, BakabaseWebProxy proxy)
    : WebCloudProvider(tool, appService, proxy)
{
    public override string ProviderId => "googleDrive";
}

public sealed class GofileCloudProvider(GalleryDlService tool, AppService appService, BakabaseWebProxy proxy)
    : WebCloudProvider(tool, appService, proxy)
{
    public override string ProviderId => "gofile";
}

public sealed class PixeldrainCloudProvider(GalleryDlService tool, AppService appService, BakabaseWebProxy proxy)
    : WebCloudProvider(tool, appService, proxy)
{
    public override string ProviderId => "pixeldrain";
}

/// <summary>The private operation manifest and completion hashes make process restarts safe to replay.</summary>
public abstract class WebCloudProvider(CloudPythonToolService tool, AppService appService, BakabaseWebProxy proxy) : ICloudDownloadProvider
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _operations = new();
    public abstract string ProviderId { get; }

    public async Task<CloudReadiness> CheckReadyAsync(string? accountId, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(accountId))
            return new CloudReadiness(false, "This provider currently supports public shares; leave the account empty.");
        try
        {
            await tool.CaptureReadyExecutionAsync(ct);
            return new CloudReadiness(true);
        }
        catch (DependencyNotInstalledException e) { return new CloudReadiness(false, e.Message); }
    }

    public async Task<CloudDownloadResult> DownloadAsync(CloudDownloadRequest request,
        Func<CloudDownloadProgress, Task> progress, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(request.AccountId) || request.RemoteEntry != null)
            throw new NotSupportedException("This provider accepts public share URLs only.");
        if (string.IsNullOrWhiteSpace(request.OperationId)) throw new ArgumentException("Operation ID is required.");
        if (!Path.IsPathFullyQualified(request.OutputDirectory)) throw new ArgumentException("An absolute output directory is required.");
        ValidateShareUrl(ProviderId, request.Source.Url);
        var execution = await tool.CaptureReadyExecutionAsync(ct);
        var operationKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.OperationId)));
        var operationLock = _operations.GetOrAdd(operationKey, _ => new SemaphoreSlim(1, 1));
        await operationLock.WaitAsync(ct);
        try
        {
            var state = Path.Combine(appService.AppDataDirectory, "cloud-downloads", "web", ProviderId, operationKey);
            var privateDirectory = appService.AppDataDirectory;
            foreach (var part in new[] { "cloud-downloads", "web", ProviderId, operationKey })
            {
                privateDirectory = Path.Combine(privateDirectory, part);
                if (new DirectoryInfo(privateDirectory).LinkTarget != null)
                    throw new InvalidDataException("Cloud operation state cannot be a symbolic link.");
                Directory.CreateDirectory(privateDirectory);
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(privateDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
            var script = Path.Combine(state, "bridge.py");
            if (new FileInfo(script).LinkTarget != null) throw new InvalidDataException("Cloud adapter script cannot be a symbolic link.");
            await using (var resource = typeof(WebCloudProvider).Assembly.GetManifestResourceStream("Bakabase.Service.CloudPythonBridge.py")
                   ?? throw new InvalidOperationException("The cloud Python adapter resource is missing."))
            await using (var target = File.Create(script)) await resource.CopyToAsync(target, ct);
            var info = execution.StartInfo(["-I", "-u", script], state);
            info.Environment["XDG_CONFIG_HOME"] = state;
            info.Environment["XDG_CACHE_HOME"] = state;
            using var process = new Process { StartInfo = info };
            process.Start();
            using var cancellation = ct.Register(() => { try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } });
            var errors = process.StandardError.BaseStream.CopyToAsync(Stream.Null, ct);
            IReadOnlyList<string>? files = null;
            string? failure = null;
            try
            {
                var input = SerializeRequest(ProviderId, request, state,
                    ResolveDownloadProxy(proxy, ProviderId, new Uri(request.Source.Url)));
                await process.StandardInput.WriteAsync(input.AsMemory(), ct);
                process.StandardInput.Close();
                while (await process.StandardOutput.ReadLineAsync(ct) is { } line)
                {
                    using var message = JsonDocument.Parse(line);
                    var root = message.RootElement;
                    switch (root.GetProperty("type").GetString())
                    {
                        case "progress":
                            await progress(new CloudDownloadProgress(Number(root, "percentage"), Integer(root, "completedBytes"),
                                Integer(root, "totalBytes"), root.GetProperty("stage").GetString()));
                            break;
                        case "error": failure = root.GetProperty("message").GetString(); break;
                        case "result":
                            if (files != null) throw new InvalidDataException("Cloud adapter returned duplicate final results.");
                            files = root.GetProperty("files").EnumerateArray().Select(x => x.GetString()!).ToArray();
                            break;
                        default: throw new InvalidDataException("Unknown cloud adapter message.");
                    }
                }
                await process.WaitForExitAsync(ct);
                await errors;
                if (process.ExitCode != 0 || failure != null || files == null)
                    throw new InvalidOperationException(failure ?? "The cloud tool exited without a complete file manifest.");
                ValidateResultFiles(request.OutputDirectory, files);
                return new CloudDownloadResult(files);
            }
            finally
            {
                if (!process.HasExited)
                {
                    try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                    await process.WaitForExitAsync(CancellationToken.None);
                }
            }
        }
        finally { operationLock.Release(); }
    }

    internal static string SerializeRequest(string providerId, CloudDownloadRequest request, string state,
        CloudToolProxy.Settings? settings) => JsonSerializer.Serialize(new
    {
        provider = providerId, url = request.Source.Url, accessCode = request.Source.AccessCode,
        output = Path.GetFullPath(request.OutputDirectory), state, proxy = settings
    }, JsonSerializerOptions.Web);

    internal static CloudToolProxy.Settings? ResolveDownloadProxy(BakabaseWebProxy proxy, string providerId, Uri destination)
    {
        var toolId = providerId switch
        {
            "googleDrive" => DownloadToolProxyTargets.Gdown,
            "gofile" or "pixeldrain" => DownloadToolProxyTargets.GalleryDl,
            _ => throw new NotSupportedException("Unsupported cloud download tool.")
        };
        return CloudToolProxy.Resolve(proxy.ForDownloadTool(toolId, CloudSites.Site(providerId)), destination);
    }

    private static double? Number(JsonElement root, string key) => root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : null;
    private static long? Integer(JsonElement root, string key) => root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt64() : null;

    internal static void ValidateShareUrl(string provider, string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort || uri.UserInfo.Length != 0)
            throw new ArgumentException("An HTTPS share URL is required.");
        var supportedHost = provider switch
        {
            "googleDrive" => uri.IdnHost is "drive.google.com" or "docs.google.com",
            "gofile" => uri.IdnHost is "gofile.io" or "www.gofile.io",
            "pixeldrain" => CloudSourceClassifier.IsPixeldrainHost(uri.IdnHost),
            _ => throw new ArgumentException("Unsupported cloud provider.")
        };
        if (!supportedHost) throw new ArgumentException("The share URL does not belong to this cloud provider.");
        // The Python adapter validates the exact URL shape before importing or calling a tool.
    }

    internal static void ValidateResultFiles(string outputDirectory, IReadOnlyList<string> files) =>
        CloudDownloadManifest.Validate(outputDirectory, files);
}
