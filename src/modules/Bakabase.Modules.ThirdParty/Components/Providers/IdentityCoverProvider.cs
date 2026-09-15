using System.Security.Cryptography;
using System.Text;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.ThirdParty.Components.Providers;

/// <summary>
/// Caches known cover URLs attached to external work identities. Identity lookup remains the
/// responsibility of the caller that created the identity; this provider never queries a site
/// for metadata or implies that the resource's files can be downloaded from that site.
/// </summary>
public class IdentityCoverProvider(
    IResourceExternalIdentityService identityService,
    IHttpClientFactory httpClientFactory,
    IFileManager fileManager,
    ILogger<IdentityCoverProvider> logger) : ICoverProvider
{
    public DataOrigin Origin => DataOrigin.ExternalIdentity;
    public int Priority => 15;

    public bool AppliesTo(Resource resource) => resource.ExternalIdentities is { Count: > 0 };

    public DataStatus GetStatus(Resource resource)
    {
        var identities = resource.ExternalIdentities;
        if (identities is not { Count: > 0 }) return DataStatus.Ready;
        if (identities.Any(i => i.LocalCoverPaths is { Count: > 0 })) return DataStatus.Ready;

        var withUrls = identities.Where(i => i.CoverUrls is { Count: > 0 }).ToList();
        if (withUrls.Count == 0) return DataStatus.Ready;
        return withUrls.Any(i => !IsBackingOff(i)) ? DataStatus.NotStarted : DataStatus.Failed;
    }

    public async Task<List<string>?> GetCoversAsync(Resource resource, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (resource.ExternalIdentities is not { Count: > 0 } identities) return null;

        var cached = identities.SelectMany(i => i.LocalCoverPaths ?? []).Distinct().ToList();
        if (cached.Count > 0) return cached;

        foreach (var identity in identities)
        {
            ct.ThrowIfCancellationRequested();
            if (identity.CoverUrls is not { Count: > 0 } || IsBackingOff(identity)) continue;

            var covers = await DownloadCoversAsync(identity, ct);
            if (covers is { Count: > 0 }) return covers;
        }

        return null;
    }

    private static bool IsBackingOff(ResourceExternalIdentity identity) =>
        identity.CoverDownloadFailedAt.HasValue &&
        DateTime.Now - identity.CoverDownloadFailedAt.Value < TimeSpan.FromHours(24);

    private async Task<List<string>?> DownloadCoversAsync(ResourceExternalIdentity identity, CancellationToken ct)
    {
        var covers = new List<string>();
        // Multiple identities of the same site can belong to one resource. Keep their cache
        // paths separate, and never use an externally supplied ID as a directory segment.
        var identityHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity.ExternalId)))
            .ToLowerInvariant();
        var coverDir = fileManager.GetSourceCoverDir("external-identity", identity.ResourceId);
        using var client = httpClientFactory.CreateClient(InternalOptions.HttpClientNames.Default);

        for (var i = 0; i < identity.CoverUrls!.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var url = identity.CoverUrls[i];
            try
            {
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                    (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
                    throw new InvalidDataException("An external cover URL must use HTTP or HTTPS.");

                using var response = await client.GetAsync(uri, ct);
                response.EnsureSuccessStatusCode();
                var contentType = response.Content.Headers.ContentType?.MediaType;
                if (contentType != null && !contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) &&
                    !contentType.Equals("application/octet-stream", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The external cover response is not an image.");

                var data = await response.Content.ReadAsByteArrayAsync(ct);
                if (data.Length == 0) throw new InvalidDataException("The external cover response is empty.");
                var extension = GetImageExtension(contentType, uri);
                var path = Path.Combine(coverDir, $"{(int)identity.ThirdPartyId}-{identityHash}-{i}{extension}");
                covers.Add(await fileManager.Save(path, data, ct));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to cache cover for external identity {IdentityId} at {Url}",
                    identity.Id, url);
            }
        }

        ct.ThrowIfCancellationRequested();
        identity.LocalCoverPaths = covers.Count > 0 ? covers : null;
        identity.CoverDownloadFailedAt = covers.Count > 0 ? null : DateTime.Now;
        await identityService.Update(identity);
        return covers.Count > 0 ? covers : null;
    }

    private static string GetImageExtension(string? contentType, Uri uri)
    {
        var fromType = contentType?.ToLowerInvariant() switch
        {
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/gif" => ".gif",
            "image/webp" => ".webp",
            "image/bmp" => ".bmp",
            "image/tiff" => ".tiff",
            "image/svg+xml" => ".svg",
            "image/avif" => ".avif",
            _ => null
        };
        if (fromType != null) return fromType;
        var extension = Path.GetExtension(uri.AbsolutePath).ToLowerInvariant();
        return InternalOptions.ImageExtensions.Contains(extension) ? extension : ".jpg";
    }

    public Task InvalidateAsync(int resourceId) => identityService.ClearLocalCoverPaths(resourceId);
}
