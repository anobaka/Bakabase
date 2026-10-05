using Bakabase.Modules.PostParser.Models.Domain;

namespace Bakabase.Modules.PostParser.Services;

public interface IPostAvailabilityAnalyzer
{
    Task<PostAvailabilityAssessment> AnalyzeAsync(PostContent content, CancellationToken ct = default);
}
