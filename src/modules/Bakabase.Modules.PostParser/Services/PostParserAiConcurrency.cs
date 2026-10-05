using Bakabase.Abstractions.Components.Threading;

namespace Bakabase.Modules.PostParser.Services;

/// <summary>One shared AI budget for both availability analysis and instruction extraction.</summary>
public sealed class PostParserAiConcurrency(Func<int>? getMaxConcurrency = null)
{
    private readonly AsyncConcurrencyGate _gate = new(getMaxConcurrency ?? (() => 1));

    public async Task<T> ExecuteAsync<T>(string stage, Func<Task<T>> action, CancellationToken ct = default)
    {
        using var lease = await _gate.EnterAsync(ct,
            () => PostParserExecutionScope.ReportStageAsync("waitingForAi", ct)).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        await PostParserExecutionScope.ReportStageAsync(stage, ct).ConfigureAwait(false);
        return await action().ConfigureAwait(false);
    }
}
