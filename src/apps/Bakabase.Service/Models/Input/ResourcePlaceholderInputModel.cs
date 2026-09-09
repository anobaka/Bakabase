using System.Collections.Generic;
using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Service.Models.Input;

/// <summary>
/// One thing the user says they are missing. Exactly one of the three shapes is expected, and the
/// server is forgiving about which: a <see cref="Title"/> that turns out to be a work id or a link
/// is routed as such, so a client can offer a single "paste one per line" box instead of making the
/// user classify each line.
/// </summary>
public record ResourcePlaceholderItemInputModel
{
    /// <summary>A name, a work id, or a link — see the type summary.</summary>
    public string? Title { get; set; }

    /// <summary>The platform, when the client already knows which one is meant.</summary>
    public ResourceSource? Source { get; set; }

    /// <summary>The id on that platform. Accepts the platform's page URL too.</summary>
    public string? SourceKey { get; set; }

    /// <summary>A link someone shared. Never an identity — it is stored as an acquisition lead.</summary>
    public string? SharedUrl { get; set; }
}

public record ResourcePlaceholderInputModel
{
    public List<ResourcePlaceholderItemInputModel> Items { get; set; } = [];

    /// <summary>
    /// Start acquiring the resources right away. Not wired up yet — the acquisition pipeline lands
    /// in a later milestone; until then the resources are created and left for the user to act on.
    /// </summary>
    public bool AcquireImmediately { get; set; }
}
