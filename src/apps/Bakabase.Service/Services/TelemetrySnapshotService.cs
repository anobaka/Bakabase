using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Bakabase.Abstractions.Services;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Infrastructures.Components.Configurations.App;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bakabase.Modules.Enhancer.Models.Domain.Constants;
using Bakabase.Service.Components;
using Bakabase.Service.Models.View;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bakabase.Service.Services;

/// <summary>
/// Computes the per-install state payload for analytics. Designed to be safe to call on
/// every app launch — counts and option reads only, no file-system scanning. The payload
/// is hash-stable so the frontend can detect "nothing changed since last upload" and skip
/// the GA4 round-trip (see design §6.5).
/// </summary>
public interface ITelemetrySnapshotService
{
    Task<TelemetrySnapshotViewModel> GenerateAsync();
}

public class TelemetrySnapshotService(
    BakabaseDbContext dbContext,
    IResourceProfileService resourceProfileService,
    IBOptions<AppOptions> appOptions,
    IBOptions<AiOptions> aiOptions,
    IWebHostEnvironment env,
    ILogger<TelemetrySnapshotService> logger) : ITelemetrySnapshotService
{
    public async Task<TelemetrySnapshotViewModel> GenerateAsync()
    {
        var version = AppService.CoreVersion.ToString();
        var releaseChannel = ReleaseChannelDetector.Detect(env);
        var os = DetectOs();
        var locale = appOptions.Value.Language ?? string.Empty;

        var mediaLibraryCount = 0;
        var resourceCount = 0;
        try
        {
            mediaLibraryCount = await dbContext.MediaLibrariesV2.CountAsync();
            resourceCount = await dbContext.ResourcesV2.CountAsync();
        }
        catch (Exception e)
        {
            // Telemetry is a best-effort dashboard signal — a disk I/O hiccup
            // or partial migration shouldn't fire a Sentry event. Drop to
            // Warning so it stays in local logs but isn't escalated.
            logger.LogWarning(e, "Telemetry: failed to count media libraries / resources");
        }

        // Sorted ordinally so the snapshot hash is stable across profile-ordering changes.
        var enabledEnhancers = new SortedSet<string>(StringComparer.Ordinal);
        try
        {
            var profiles = await resourceProfileService.GetAll();
            foreach (var profile in profiles)
            {
                var ids = profile.EnhancerOptions?.Enhancers?.Select(e => e.EnhancerId) ?? [];
                foreach (var id in ids)
                {
                    if (Enum.IsDefined(typeof(EnhancerId), id))
                    {
                        enabledEnhancers.Add(((EnhancerId)id).ToString());
                    }
                }
            }
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Telemetry: failed to enumerate enabled enhancers");
        }

        var aiEnabled = aiOptions.Value.DefaultProviderConfigId.HasValue;

        return new TelemetrySnapshotViewModel
        {
            AppVersion = version,
            ReleaseChannel = releaseChannel,
            Os = os,
            Locale = locale,
            MediaLibraryCount = mediaLibraryCount,
            ResourceCount = resourceCount,
            EnabledEnhancers = enabledEnhancers.ToList(),
            AiEnabled = aiEnabled,
            HasMediaLibrary = mediaLibraryCount > 0,
        };
    }

    private static string DetectOs()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return "windows";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return "macos";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return "linux";
        return "other";
    }
}
