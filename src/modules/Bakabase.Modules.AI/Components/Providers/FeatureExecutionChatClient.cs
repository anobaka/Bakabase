using System.Runtime.CompilerServices;
using Bakabase.Modules.AI.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.AI.Components.Providers;

/// <summary>Applies feature capacity to real provider calls, inside the optional cache.</summary>
internal sealed class FeatureExecutionChatClient(IChatClient innerClient, ILlmFeatureExecutionPolicy policy, ILogger logger)
    : DelegatingChatClient(innerClient)
{
    public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages,
        ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var lease = await policy.EnterAsync(cancellationToken).ConfigureAwait(false);
        try { return await base.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false); }
        finally { await ReleaseAsync(lease).ConfigureAwait(false); }
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var lease = await policy.EnterAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken)
                               .ConfigureAwait(false))
                yield return update;
        }
        finally { await ReleaseAsync(lease).ConfigureAwait(false); }
    }

    private async Task ReleaseAsync(IDisposable lease)
    {
        lease.Dispose();
        try
        {
            await LlmExecutionScope.ReportAsync(new(policy.CapabilityId, policy.Feature,
                LlmFeatureExecutionStatus.Released), CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            // Progress cleanup cannot turn a completed inference into failure or hide its error.
            logger.LogDebug(error, "Could not report released AI capability {CapabilityId}", policy.CapabilityId);
        }
    }
}
