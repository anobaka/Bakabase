using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Components.DownloadIntents;

namespace Bakabase.Service.Components.Acquisition.Planning;

/// <summary>The producer boundary only admits explicitly grouped mirrors in the exact saved revision.
/// Reading does not refresh/reparse the post, purchase content or infer a missing relationship.</summary>
public sealed class PostSourceRevisionCandidates(PostDownloadIntentAdapter reader) : IAcquisitionSourceRevisionCandidates
{
    public async Task<IReadOnlyList<AcquisitionSourceRevisionCandidate>> ReadAsync(AcquisitionPlan plan,
        IReadOnlyList<WorkflowSourceRef> sourceRefs, CancellationToken ct)
    {
        var output = new List<AcquisitionSourceRevisionCandidate>();
        foreach (var origin in sourceRefs.Where(s => s.Module == "postParser" && s.EntityKind == "task")
                     .Select(s => s with {ResultKey = null}).Distinct())
        {
            if (!int.TryParse(origin.EntityId, out var id) || !int.TryParse(origin.Revision, out var revision)) continue;
            // Read enforces saved revision, complete/stable parsing and preview exclusion. Effective
            // processing instructions are returned per source, never borrowed from the old mirror.
            var saved = await reader.Read(new() {TaskId = id, Revision = revision}, false, ct);
            foreach (var part in plan.Parts)
            {
                var current = part.Sources.SingleOrDefault(s => s.Id == part.SelectedSourceId);
                var original = saved.Input.Candidates.SingleOrDefault(c => c.Id == current?.Id &&
                    (c.Url == current.Url || c.Url == current.OriginalUrl));
                if (original?.MirrorGroupConfirmed != true || string.IsNullOrWhiteSpace(original.GroupId)) continue;
                foreach (var candidate in saved.Input.Candidates.Where(c => c.Id != original.Id &&
                             c.MirrorGroupConfirmed == true && c.GroupId == original.GroupId))
                {
                    FileProcessingPlan? processing;
                    try { processing = string.IsNullOrWhiteSpace(candidate.ExtractionPlanJson) ? new() :
                        FileProcessingPlanValidator.Validate(JsonSerializer.Deserialize<FileProcessingPlan>(candidate.ExtractionPlanJson, JsonSerializerOptions.Web)); }
                    catch (Exception e) when (e is JsonException or ArgumentException or InvalidOperationException) { continue; }
                    var sourceRef = origin with {ResultKey = candidate.Id};
                    var effective = saved.ProcessingPlans.GetValueOrDefault(candidate.Id);
                    var source = new AcquisitionPlanSource {Id = candidate.Id, Url = candidate.Url, Kind = candidate.Kind,
                        ProviderId = candidate.ProviderId, AccessCode = candidate.AccessCode, ArchivePassword = candidate.ArchivePassword,
                        MethodId = candidate.MethodId, AccountId = candidate.AccountId, Processing = processing, ProcessingSource = sourceRef,
                        AppliedProcessingRevisionId = effective is {EffectiveRevision: > 0} ? effective.History.First().Id : null};
                    var evidence = $"postParser:{id}:{revision}:group:{original.GroupId}";
                    output.Add(new($"post:{id}:{revision}:{candidate.Id}", part.Id, source, [origin, sourceRef], evidence,
                        CloudOperationService.Key(evidence, JsonSerializer.Serialize(saved.Input.Candidates, JsonSerializerOptions.Web),
                            JsonSerializer.Serialize(source, JsonSerializerOptions.Web))));
                }
            }
        }
        return output;
    }
}
