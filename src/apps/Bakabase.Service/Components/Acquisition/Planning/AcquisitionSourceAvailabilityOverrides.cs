using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Components.DownloadIntents;

namespace Bakabase.Service.Components.Acquisition.Planning;

/// <summary>A manual decision is local to one task, exact locator and resolved account generation.</summary>
internal static class AcquisitionSourceAvailabilityOverrides
{
    internal static string SourceFingerprint(AcquisitionPlanSource source) => CloudOperationService.Key(JsonSerializer.Serialize(new
    {
        source.Id, source.Url, source.Kind, source.ProviderId, source.MethodId, source.AccountId,
        source.AccessCode, source.ArchivePassword
    }));

    internal static string ResolvedFingerprint(CloudSource source) => CloudSourceIdentity.Fingerprint(source);

    internal static AcquisitionSourceAvailabilityOverride Create(int taskId, AcquisitionPlanSource source, CloudSource resolved) =>
        new(taskId, SourceFingerprint(source), ResolvedFingerprint(resolved), DateTimeOffset.UtcNow);

    internal static bool Matches(AcquisitionPlanSource source, int taskId, CloudSource? resolved = null) =>
        source.ManualAvailabilityOverride is { } grant && grant.TaskId == taskId &&
        grant.SourceFingerprint == SourceFingerprint(source) &&
        (resolved == null || grant.ResolvedSourceFingerprint == ResolvedFingerprint(resolved));

    internal static IReadOnlyDictionary<string, string> ForRequest(AcquisitionPlan plan, string? contextKey)
    {
        if (contextKey?.StartsWith("acquisition:", StringComparison.Ordinal) != true ||
            !int.TryParse(contextKey["acquisition:".Length..], out var taskId)) return new Dictionary<string, string>();
        return plan.Parts.Where(p => p.SelectionMode == "manual" && p.ContinueFrom == null)
            .SelectMany(p => p.Sources.Where(s => s.Id == p.SelectedSourceId && Matches(s, taskId)))
            .ToDictionary(s => s.Id, s => s.ManualAvailabilityOverride!.ResolvedSourceFingerprint, StringComparer.Ordinal);
    }
}
