using System.Collections.Generic;
using System.Linq;
using Bakabase.Modules.PostParser.Models.Domain;
using Bakabase.Service.Components.DownloadIntents;
using Bakabase.Service.Components.Acquisition.Planning;

namespace Bakabase.Service.Components.Acquisition.Handoffs;

/// <summary>Only explicit main-content groups establish mirrors. Unknown relationships need review.</summary>
public static class PostAcquisitionSelection
{
    public static IReadOnlyList<IReadOnlyList<string>>? Groups(PostDownloadInfo info, IReadOnlyList<DownloadCandidate> candidates)
    {
        // Saved result indices remain untouched. Collapse exact duplicate locators only in this
        // selection projection; differing credentials, instructions or content groups stay distinct.
        candidates = candidates.DistinctBy(c => (c.Url, c.AccessCode, c.ArchivePassword, c.ExtractionPlanJson,
            c.GroupId, c.Kind, c.ProviderId, c.MethodId, c.AccountId)).ToList();
        if (candidates.Count == 0) return [];
        var declared = info.Groups ?? [];
        if (declared.Any(g => g == null) || declared.GroupBy(g => g.Id).Any(g => g.Count() > 1)) return null;
        var known = declared.ToDictionary(g => g.Id);
        var main = candidates.Where(c => c.GroupId != null && known.GetValueOrDefault(c.GroupId)?.Kind == "main").ToList();
        var uncertain = candidates.Where(c => c.GroupId == null || !known.TryGetValue(c.GroupId, out var group) || group.Kind == "unknown").ToList();
        if (uncertain.Count > 0)
            return candidates.Count == 1 ? [new[] {candidates[0].Id}] : null;
        return main.GroupBy(c => c.GroupId).Select(g => (IReadOnlyList<string>)g.Select(c => c.Id).ToList()).ToList();
    }

    public static IReadOnlyList<DownloadCandidateView> Choose(IReadOnlyList<IReadOnlyList<string>> groups,
        IReadOnlyList<DownloadCandidateView> checkedCandidates, IReadOnlyList<string>? providerPriority = null) => groups.Select(group =>
        AcquisitionSourceSelectionPolicy.Choose(checkedCandidates.Where(c => group.Contains(c.Candidate.Id)), providerPriority)).ToList();
}
