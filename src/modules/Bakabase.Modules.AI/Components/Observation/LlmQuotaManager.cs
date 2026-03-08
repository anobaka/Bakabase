using Bakabase.Modules.AI.Models.Domain;
using Microsoft.Extensions.Options;

namespace Bakabase.Modules.AI.Components.Observation;

public class LlmQuotaManager(
    IOptionsMonitor<AiModuleOptions> aiOptions,
    ILlmUsageService usageService
)
{
    public async Task CheckQuotaAsync(CancellationToken ct = default)
    {
        var quota = aiOptions.CurrentValue.Quota;
        if (quota == null) return;

        if (quota.DailyTokenLimit.HasValue)
        {
            var todayUsage = await usageService.GetTodayTokenUsageAsync(ct);
            if (todayUsage >= quota.DailyTokenLimit.Value)
            {
                throw new LlmQuotaExceededException(
                    $"Daily token limit exceeded: {todayUsage}/{quota.DailyTokenLimit.Value}");
            }
        }

        if (quota.MonthlyTokenLimit.HasValue)
        {
            var monthUsage = await usageService.GetMonthTokenUsageAsync(ct);
            if (monthUsage >= quota.MonthlyTokenLimit.Value)
            {
                throw new LlmQuotaExceededException(
                    $"Monthly token limit exceeded: {monthUsage}/{quota.MonthlyTokenLimit.Value}");
            }
        }
    }
}

public class LlmQuotaExceededException(string message) : Exception(message);
