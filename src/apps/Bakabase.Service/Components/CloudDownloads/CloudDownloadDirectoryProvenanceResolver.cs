using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Service.Components.DownloadIntents;

namespace Bakabase.Service.Components.CloudDownloads;

/// <summary>Factories remain dormant until one completed download needs local receipt inspection.</summary>
public sealed record CloudDownloadDirectoryProvenanceRegistration(string ProviderId, string? MethodId,
    Func<IServiceProvider, ICloudDownloadDirectoryProvenanceProvider> Create);

public sealed class CloudDownloadDirectoryProvenanceResolver(IServiceProvider services,
    IEnumerable<CloudDownloadDirectoryProvenanceRegistration> registrations)
{
    public Task<CloudDownloadDirectoryEvidence?> InspectAsync(CloudDownloadRequest request, string root, CancellationToken ct)
    {
        if (request.RemoteEntry?.OwnedContainer == null) return Task.FromResult<CloudDownloadDirectoryEvidence?>(null);
        var method = DownloadMethodCatalog.Select(request.Source);
        if (method == null) return Task.FromResult<CloudDownloadDirectoryEvidence?>(null);
        var candidates = registrations.Where(r => r.ProviderId == request.Source.ProviderId).ToArray();
        var registration = candidates.SingleOrDefault(r => r.MethodId == method.Id) ??
            (method.EngineId == "rclone" ? null : candidates.SingleOrDefault(r => r.MethodId == null));
        return registration == null ? Task.FromResult<CloudDownloadDirectoryEvidence?>(null) :
            registration.Create(services).InspectDirectoryEvidenceAsync(request, root, ct);
    }
}
