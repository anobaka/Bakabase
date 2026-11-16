using System;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Modules.PostParser.Models.Domain;
using Bakabase.Modules.PostParser.Services;
using Bakabase.Service.Components.DownloadIntents;

namespace Bakabase.Service.Components.SourceAvailability;

/// <summary>The post-parser boundary only maps observations; source semantics remain in shared probes.</summary>
public sealed class SharedPostLinkHealthChecker(SourceAvailabilityService availability) : IPostLinkHealthChecker
{
    public async Task<PostLinkHealth> CheckAsync(string url, string? accessCode, CancellationToken ct = default)
    {
        CloudSource source;
        try {source = DownloadMethodRegistry.Normalize(new DownloadCandidate {Id = "link", Url = url, AccessCode = accessCode});}
        catch (ArgumentException) {return new() {Status = "unknown", Reason = "SOURCE_INVALID", ReasonCode = "SOURCE_INVALID"};}
        var result = await availability.CheckAsync(source, false, ct);
        return new() {Status = result.Status == SourceAvailabilityStatuses.ConfirmedUnavailable ? "unavailable" : result.Status,
            Reason = result.ReasonCode, ReasonCode = result.ReasonCode, CheckedAt = result.CheckedAt,
            ExpiresAt = result.ExpiresAt, ProbeId = result.ProbeId, ProbeVersion = result.ProbeVersion, Evidence = result.Evidence};
    }
}
