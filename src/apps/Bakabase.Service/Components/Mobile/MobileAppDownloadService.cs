using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Service.Models.View;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Bakabase.Service.Components.Mobile
{
    /// <summary>
    /// Discovers where the mobile app can be downloaded from. The URLs only
    /// exist once CI has published packages, so CI writes a manifest to a fixed
    /// CDN path (see scripts/mobile/build_mobile_manifest.py) and this service
    /// fetches and caches it.
    /// </summary>
    public class MobileAppDownloadService(ILogger<MobileAppDownloadService> logger)
    {
        private const string ManifestUrl = "https://cdn-public.anobaka.com/app/bakabase-mobile/manifest.json";
        private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(1);

        private static readonly HttpClient Http = new() {Timeout = TimeSpan.FromSeconds(10)};

        private readonly SemaphoreSlim _lock = new(1, 1);
        private MobileAppDownloadsViewModel? _cached;
        private DateTime _fetchedAt = DateTime.MinValue;

        /// <summary>
        /// The latest manifest, or null when it cannot be fetched and nothing is
        /// cached — the UI then explains instead of erroring.
        /// </summary>
        public async Task<MobileAppDownloadsViewModel?> GetAsync(CancellationToken ct)
        {
            if (_cached != null && DateTime.UtcNow - _fetchedAt < CacheTtl)
            {
                return _cached;
            }

            await _lock.WaitAsync(ct);
            try
            {
                if (_cached != null && DateTime.UtcNow - _fetchedAt < CacheTtl)
                {
                    return _cached;
                }

                // The CDN caches the manifest object; a fresh query string asks it
                // for the origin's current version without needing a cache purge.
                var url = $"{ManifestUrl}?t={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
                var json = await Http.GetStringAsync(url, ct);
                var manifest = JsonConvert.DeserializeObject<MobileAppDownloadsViewModel>(json);

                if (manifest?.Files is {Count: > 0})
                {
                    _cached = manifest;
                    _fetchedAt = DateTime.UtcNow;
                }

                return _cached;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // Offline host, DNS failure, nothing published yet — all fine;
                // yesterday's cache (if any) keeps serving.
                logger.LogWarning(e, "Could not fetch the mobile download manifest");
                return _cached;
            }
            finally
            {
                _lock.Release();
            }
        }
    }
}
