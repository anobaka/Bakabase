using Bakabase.Modules.AI.Models.Db;
using Bakabase.Modules.AI.Models.Domain;

namespace Bakabase.Modules.AI.Components.Observation;

public interface ILlmUsageService
{
    Task TrackAsync(LlmUsageLogDbModel log, CancellationToken ct = default);
    Task<IReadOnlyList<LlmUsageLogDbModel>> SearchAsync(LlmUsageSearchCriteria criteria, CancellationToken ct = default);
    Task<LlmUsageSummary> GetSummaryAsync(CancellationToken ct = default);

    /// <summary>
    /// Get total tokens used today for quota checking.
    /// </summary>
    Task<long> GetTodayTokenUsageAsync(CancellationToken ct = default);

    /// <summary>
    /// Get total tokens used this month for quota checking.
    /// </summary>
    Task<long> GetMonthTokenUsageAsync(CancellationToken ct = default);
}

public record LlmUsageSearchCriteria
{
    public int? ProviderConfigId { get; init; }
    public string? ModelId { get; init; }
    public string? Feature { get; init; }
    public DateTime? StartTime { get; init; }
    public DateTime? EndTime { get; init; }
    public int PageIndex { get; init; }
    public int PageSize { get; init; } = 50;
}

public record LlmUsageSummary
{
    public long TotalTokens { get; init; }
    public long TodayTokens { get; init; }
    public long MonthTokens { get; init; }
    public int TotalCalls { get; init; }
    public int CacheHits { get; init; }
    public double CacheHitRate { get; init; }
    public IReadOnlyList<LlmUsageByProvider> ByProvider { get; init; } = [];
    public IReadOnlyList<LlmUsageByFeature> ByFeature { get; init; } = [];
}

public record LlmUsageByProvider
{
    public int ProviderConfigId { get; init; }
    public string ModelId { get; init; } = string.Empty;
    public long TotalTokens { get; init; }
    public int CallCount { get; init; }
}

public record LlmUsageByFeature
{
    public string Feature { get; init; } = string.Empty;
    public long TotalTokens { get; init; }
    public int CallCount { get; init; }
}
