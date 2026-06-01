namespace Bakabase.Modules.Workflow.Abstractions.Components;

/// <summary>
/// Generic event-publication API. Producers (e.g. <c>SubscriptionService</c>) call
/// <see cref="PublishAsync"/>; the implementation finds matching workflow definitions,
/// records pending runs, and enqueues each run as a BTask.
/// </summary>
public interface IWorkflowEventBus
{
    Task PublishAsync<T>(string triggerKind, T payload, CancellationToken ct = default);
}
