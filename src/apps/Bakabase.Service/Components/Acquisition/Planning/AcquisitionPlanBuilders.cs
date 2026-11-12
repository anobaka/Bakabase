using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Service.Components.CloudDownloads;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.ThirdParty.ThirdParties.ExHentai;
using Bakabase.Modules.ThirdParty.ThirdParties.ExHentai.Models.RequestModels;

namespace Bakabase.Service.Components.Acquisition.Planning;

public interface IAcquisitionPlanBuilder
{
    AcquisitionPlanBuilderDescriptor Descriptor { get; }
    Task<IReadOnlyList<AcquisitionPlanCandidate>> SearchAsync(string query, CancellationToken ct);
    AcquisitionPlan BuildPlan(AcquisitionPlanCandidate candidate, string title, string outputDirectory);
}
public sealed class ExHentaiAcquisitionPlanBuilder(ExHentaiClient client) : IAcquisitionPlanBuilder
{
    public AcquisitionPlanBuilderDescriptor Descriptor => new("exhentai.search",
        "acquisition.plan.builder.exhentai.name", "acquisition.plan.builder.exhentai.description", ["manual"], "title");
    public async Task<IReadOnlyList<AcquisitionPlanCandidate>> SearchAsync(string query, CancellationToken ct)
    {
        var results = await client.Search(new ExHentaiSearchRequestModel {Keyword = query}, ct, includeMetadata: false);
        return (results.Resources ?? []).Take(50).Select(r => new AcquisitionPlanCandidate(r.Id.ToString(),
            string.IsNullOrWhiteSpace(r.Name) ? r.RawName : r.Name, r.Url, r.CoverUrl))
            .DistinctBy(r => r.Id).ToList();
    }
    public AcquisitionPlan BuildPlan(AcquisitionPlanCandidate candidate, string title, string outputDirectory)
    {
        var sourceId = CloudOperationService.Key(candidate.Url);
        var covers = Uri.TryCreate(candidate.CoverUrl, UriKind.Absolute, out var cover) &&
            cover.Scheme is "http" or "https" && cover.UserInfo.Length == 0 && candidate.CoverUrl!.Length <= 2048
                ? new List<AcquisitionCoverCandidate> {new() {Url = candidate.CoverUrl, Kind = "cover"}} : [];
        return new() {Title = title, OutputDirectory = outputDirectory, CoverCandidates = covers, Parts = [new()
        {
            Id = "main", Title = candidate.Title, SelectedSourceId = sourceId,
            Sources = [new() {Id = sourceId, Url = candidate.Url, Kind = "auto", MethodId = "site.exHentai"}],
            Processing = new() {Requirement = "notRequired"}
        }]};
    }

}
