namespace Bakabase.Abstractions.Models.View;

/// <summary>
/// The outcome of asking for a move.
/// </summary>
/// <param name="BatchId">Identifies the batch that was created.</param>
/// <param name="SkippedResourceCount">
/// How many of the selected resources were left out because they have no local files. Such a
/// resource is known to Bakabase but not materialized on disk yet, so there is nothing to move —
/// silently dropping it would leave the user thinking their whole selection was moved.
/// </param>
public record ResourceMoveBatchViewModel(string BatchId, int SkippedResourceCount);
