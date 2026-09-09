namespace Bakabase.Modules.ThirdParty.ThirdParties.SoulPlus.Models;

/// <summary>One thread as a board's list page shows it.</summary>
public record SoulPlusThread
{
    /// <summary>The forum's own id for the thread. Unique within the site, and stable.</summary>
    public string Tid { get; set; } = null!;

    public string Title { get; set; } = null!;

    /// <summary>The thread's page — which is also the link someone would follow to get the thing.</summary>
    public string Url { get; set; } = null!;
}
