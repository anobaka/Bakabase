using System;
using System.Collections.Generic;
using System.Linq;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Service.Components.DownloadIntents;

namespace Bakabase.Service.Components.Acquisition.Planning;

/// <summary>Only ranks explicit same-content candidates. It never drops a required part or executes a source.</summary>
public static class AcquisitionSourceSelectionPolicy
{
    public static DownloadCandidateView Choose(IEnumerable<DownloadCandidateView> candidates,
        IReadOnlyList<string>? providerPriority = null)
    {
        var priority = (providerPriority ?? []).Select((id, index) => (id, index))
            .GroupBy(p => p.id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().index, StringComparer.Ordinal);
        // Eligibility excludes unsupported, disabled and confirmed-unavailable sources.
        // An inconclusive probe or transient network problem must not override the
        // user's provider preference; health only breaks ties at the same priority.
        return candidates.OrderBy(c => c.CanSelect ? 0 : 1)
            .ThenBy(c => priority.GetValueOrDefault(c.ResolvedSource?.ProviderId ?? c.Candidate.ProviderId ?? "", int.MaxValue))
            .ThenBy(c => AvailabilityRank(c))
            .First();
    }

    private static int AvailabilityRank(DownloadCandidateView candidate) =>
        candidate.Availability is not { } result || result.ExpiresAt <= DateTimeOffset.UtcNow ? 1 : result.Status switch
        {
            SourceAvailabilityStatuses.Available => 0,
            SourceAvailabilityStatuses.TemporarilyUnavailable => 2,
            SourceAvailabilityStatuses.AccessRequired => 3,
            SourceAvailabilityStatuses.ConfirmedUnavailable => 4,
            _ => 1
        };

    /// <summary>Used only after the service has proved this task never had a run. Legacy/manual parts stay fixed.</summary>
    public static AcquisitionPlan ApplyPreStartFallback(AcquisitionPlan plan, IReadOnlyList<DownloadCandidateView> candidates)
    {
        var byId = candidates.ToDictionary(c => c.Candidate.Id, StringComparer.Ordinal);
        var changed = false;
        var parts = plan.Parts.Select(part =>
        {
            if (part.SelectionMode != "automatic" || part.Sources.Count < 2 ||
                !byId.TryGetValue(part.SelectedSourceId!, out var current) || current.CanSelect) return part;
            var alternatives = part.Sources.Where(s => s.Id != part.SelectedSourceId && s.ProcessingIssueCode == null && byId.TryGetValue(s.Id, out var c) && c.CanSelect)
                .Select(s => byId[s.Id]).ToList();
            if (alternatives.Count == 0) return part;
            var choice = Choose(alternatives, plan.SourceProviderPriority);
            var source = part.Sources.Single(s => s.Id == choice.Candidate.Id);
            changed = true;
            return part with {SelectedSourceId = source.Id, Processing = source.Processing,
                ProcessingSource = source.ProcessingSource, AppliedProcessingRevisionId = source.AppliedProcessingRevisionId};
        }).ToList();
        return changed ? plan with {Parts = parts} : plan;
    }
}
