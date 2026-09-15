namespace Bakabase.Service.Models.View;

/// <summary>
/// What happened to one requested item. Results come back in the order they were sent, and one item
/// failing never stops the rest — pasting twenty lines with one typo should still create nineteen
/// resources.
/// </summary>
/// <param name="Index">Position in the request, so a client can line results up with its own rows.</param>
/// <param name="ResourceId">Null only when the item failed.</param>
/// <param name="Created">False when an existing resource already stood for this.</param>
/// <param name="Name">What the resource is called.</param>
/// <param name="Error">Why the item failed, when it did.</param>
public record ResourcePlaceholderResultViewModel(
    int Index,
    int? ResourceId,
    bool Created,
    string? Name,
    string? Error);
