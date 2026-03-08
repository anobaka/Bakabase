namespace Bakabase.Modules.AI.Models.Domain;

public record LlmQuotaConfig
{
    public int? DailyTokenLimit { get; set; }
    public int? MonthlyTokenLimit { get; set; }
}
