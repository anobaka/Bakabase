using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Dependency.Exceptions;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.BaiduPcsGo;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Service.Components.CloudDownloads.Baidu;
using Bakabase.Service.Components.Workflow;

namespace Bakabase.Service.Components.SourceAvailability.Probes;

/// <summary>Anonymous, isolated metadata process. No account profile, account gate or transfer journal is opened.</summary>
public sealed class BaiduShareAvailabilityProbe(BaiduPcsBridgeClient bridge, BaiduPcsGoService dependency)
    : ISourceAvailabilityProbe
{
    private const int Version = 3;
    public int CacheVersion => Version;
    public bool CanHandle(CloudSource source) => CloudSourceClassifier.Classify(source.Url) == "baidu";

    public async Task<SourceAvailabilityResult> CheckAsync(CloudSource source, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(25));
        try
        {
            // probe-share returns before the bridge's profile initialization; this directory
            // is a working directory only and the command cannot write into it.
            var result = await bridge.ExecuteAsync(Path.GetDirectoryName(dependency.Executable)!,
                new { command = "probe-share", url = source.Url, accessCode = source.AccessCode }, null, deadline.Token);
            return Classify(result.TryGetProperty("code", out var code) ? code.GetString() : null);
        }
        catch (DependencyNotInstalledException) { return Classify("BAIDU_DEPENDENCY_REQUIRED"); }
        catch (CloudProviderException error) { return Classify(error.Message); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return Classify("BAIDU_SHARE_NETWORK_ERROR"); }
        catch (IOException) { return Classify("BAIDU_PROBE_UNAVAILABLE"); }
    }

    internal static SourceAvailabilityResult Classify(string? code)
    {
        var status = ExecutionProblemClassifier.AvailabilityStatus(code);
        var reason = ExecutionProblemClassifier.NormalizedReason(code) ?? "SOURCE_AVAILABILITY_UNDETERMINED";
        return AvailabilityProbeResults.Create("baidu.share-metadata", status, reason,
            new() { ["scope"] = "anonymous", ["response"] = reason }, version: Version);
    }
}
