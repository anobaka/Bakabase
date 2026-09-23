namespace Bakabase.Remoting.Abstractions.Models;

/// <summary>
/// One library as the server sees it, and where the same files are on this machine.
/// </summary>
/// <remarks>
/// The two halves of a split install genuinely disagree about where files are: a server
/// in a container serves <c>/data/media</c>, and the same share is <c>Z:\media</c> on the
/// desktop looking at it. Nothing can infer that — only the person who mounted it knows —
/// so it is configuration, and an unmapped path is refused rather than guessed at.
/// </remarks>
public class ClientPathMapping
{
    /// <summary>The prefix as the server writes it, e.g. <c>/data/media</c>.</summary>
    public string ServerPath { get; set; } = null!;

    /// <summary>The same place on this machine, e.g. <c>Z:\media</c>.</summary>
    public string LocalPath { get; set; } = null!;
}
