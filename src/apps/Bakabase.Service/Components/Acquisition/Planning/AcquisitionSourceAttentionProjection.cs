using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Components.DownloadIntents;
using Bakabase.Service.Components.SourceAvailability;
using Bakabase.Service.Components.Workflow;

namespace Bakabase.Service.Components.Acquisition.Planning;

/// <summary>Projects existing source evidence without probing, changing a plan or resuming execution.
/// All mirrors of a required part must be unavailable before that part is classified unavailable.</summary>
public sealed class AcquisitionSourceAttentionProjection(SourceAvailabilityService availability)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<string>> ProjectAsync(string? planJson, IReadOnlyList<string> codes,
        IReadOnlyDictionary<string, IReadOnlyList<string>> partCodes, DateTimeOffset? executionAt,
        CancellationToken ct, IReadOnlySet<string>? acquiredPartIds = null, string? activityKind = null)
    {
        // Old activities sometimes recorded only a generic warning (or no recognized machine code).
        // Such a warning is source-related only while a known source acquisition step is stopped.
        var legacySourceDiagnostic = (CloudWorkflow.Nodes.Contains(activityKind) || activityKind == "baidu.download") &&
            codes.All(c => c == "SOURCE_ATTENTION_REQUIRED" || ExecutionProblemClassifier.IsRefreshableSourceCode(c));
        bool Refreshable(string code) => ExecutionProblemClassifier.IsRefreshableSourceCode(code) ||
            legacySourceDiagnostic && code == "SOURCE_ATTENTION_REQUIRED";
        if ((!codes.Any(Refreshable) && !legacySourceDiagnostic) || string.IsNullOrWhiteSpace(planJson) ||
            planJson.Length > 1024 * 1024) return codes;
        AcquisitionPlan? plan;
        try { plan = JsonSerializer.Deserialize<AcquisitionPlan>(planJson, Json); }
        catch (JsonException) { return codes; }
        if (plan?.Parts is not {Count: > 0}) return codes;
        var parts = plan.Parts.Where(p => partCodes.ContainsKey(p.Id)).ToArray();
        var requireAllPartsUnavailable = false;
        // An unbound diagnostic cannot identify which already-downloaded part failed.
        if (parts.Length == 0)
        {
            if (partCodes.Count > 0) return codes;
            if (plan.Parts.Count != 1)
            {
                // No attribution is needed if every part has newer explicit absence evidence.
                // Partial evidence must not attach an old generic failure to an arbitrary part.
                if (!legacySourceDiagnostic || codes.Any(c => c != "SOURCE_ATTENTION_REQUIRED") ||
                    acquiredPartIds is {Count: > 0} || plan.Parts.Any(p => p.ContinueFrom != null)) return codes;
                requireAllPartsUnavailable = true;
            }
            parts = plan.Parts.ToArray();
        }
        // A dead original link does not invalidate files already obtained for processing.
        parts = parts.Where(p => p.ContinueFrom == null && acquiredPartIds?.Contains(p.Id) != true).ToArray();
        if (parts.Length == 0) return codes;
        var projected = codes.Where(c => !Refreshable(c)).ToList();
        if (partCodes.Count > 0)
        {
            var boundCodes = parts.SelectMany(p => partCodes.GetValueOrDefault(p.Id) ?? []).ToHashSet(StringComparer.Ordinal);
            projected.AddRange(codes.Where(c => Refreshable(c) && !boundCodes.Contains(c)));
            var boundParts = parts.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
            projected.AddRange(partCodes.Where(p => !boundParts.Contains(p.Key)).SelectMany(p => p.Value)
                .Where(Refreshable));
        }
        var unavailableParts = 0;
        foreach (var part in parts)
        {
            var partPrior = partCodes.GetValueOrDefault(part.Id) ?? codes;
            var prior = partPrior.Where(Refreshable).ToArray();
            var genericPrior = legacySourceDiagnostic && partPrior.All(c => c == "SOURCE_ATTENTION_REQUIRED");
            if (prior.Length == 0 && !genericPrior) continue;
            var observations = new List<(bool Selected, string Status, string? Code, bool Fresh, bool Applicable)>();
            foreach (var source in part.Sources)
            {
                var selected = source.Id == part.SelectedSourceId || part.SelectedSourceId == null && part.Sources.Count == 1;
                SourceAvailabilityResult? result = null;
                try
                {
                    var normalized = DownloadMethodRegistry.Normalize(new DownloadCandidate
                    {Id = source.Id, Url = source.Url, Kind = source.Kind, ProviderId = source.ProviderId,
                        MethodId = source.MethodId, AccountId = source.AccountId, AccessCode = source.AccessCode});
                    result = await availability.GetAsync(normalized, ct);
                }
                catch (ArgumentException) { /* An invalid locator is not proof of remote expiry. */ }
                var fresh = result != null && result.ExpiresAt > DateTimeOffset.UtcNow &&
                    (executionAt == null || result.CheckedAt >= executionAt);
                // TTL controls new decisions, not whether the UI remembers explicit evidence.
                // The runtime source detail retains its CheckedAt/ExpiresAt and marks it historical.
                var applicable = fresh || result is {Status: SourceAvailabilityStatuses.ConfirmedUnavailable} &&
                    (executionAt == null || result.CheckedAt >= executionAt);
                // Fill a legacy diagnostic only from definitive evidence. Confirmed absence
                // remains historical display evidence after TTL, just as for modern records;
                // the action service still requires a fresh check before disabling a source.
                if (genericPrior) applicable = applicable && result?.Status is
                    SourceAvailabilityStatuses.Available or SourceAvailabilityStatuses.ConfirmedUnavailable;
                if (selected && prior.Any(c => ExecutionProblemClassifier.AvailabilityStatus(c) == SourceAvailabilityStatuses.ConfirmedUnavailable) &&
                    result?.Status is not (SourceAvailabilityStatuses.Available or SourceAvailabilityStatuses.ConfirmedUnavailable)) applicable = fresh = false;
                var code = applicable ? result!.ReasonCode : selected ? prior.FirstOrDefault() : null;
                observations.Add((selected, applicable ? result!.Status : ExecutionProblemClassifier.AvailabilityStatus(code), code, fresh, applicable));
            }
            if (!observations.Any(o => o.Applicable)) { projected.AddRange(prior); continue; }
            if (observations.Count > 0 && observations.All(o => o.Status == SourceAvailabilityStatuses.ConfirmedUnavailable))
            {
                unavailableParts++;
                projected.Add(observations.Count == 1 && observations[0].Code is { } reason && ExecutionProblemClassifier.IsKnown(reason)
                    ? reason : "SOURCE_CONFIRMED_UNAVAILABLE");
                continue;
            }
            var active = observations.FirstOrDefault(o => o.Selected);
            if (active.Status == SourceAvailabilityStatuses.ConfirmedUnavailable)
            {
                projected.Add(observations.Any(o => o.Status == SourceAvailabilityStatuses.Available)
                    ? "SOURCE_SELECTION_REQUIRED" : "SOURCE_VALIDITY_UNKNOWN");
            }
            else if (active.Fresh && active.Status == SourceAvailabilityStatuses.Available)
                projected.Add("SOURCE_CHECK_PASSED_RETRY_REQUIRED");
            else if (active.Code != null && ExecutionProblemClassifier.IsKnown(active.Code)) projected.Add(active.Code);
            else projected.Add("SOURCE_VALIDITY_UNKNOWN");
        }
        if (requireAllPartsUnavailable)
            return unavailableParts == parts.Length ? ["SOURCE_CONFIRMED_UNAVAILABLE"] : codes;
        return projected.Distinct(StringComparer.Ordinal).ToArray();
    }
}
