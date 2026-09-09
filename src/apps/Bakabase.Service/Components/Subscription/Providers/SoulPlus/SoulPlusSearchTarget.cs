using System.Collections.Generic;

namespace Bakabase.Service.Components.Subscription.Providers.SoulPlus;

/// <summary>What a SoulPlus subscription watches.</summary>
public record SoulPlusSearchTarget
{
    /// <summary>A board's list page, as the user would paste it from their browser.</summary>
    public string Url { get; set; } = "";

    /// <summary>
    /// Only threads whose title contains one of these. Empty watches the whole board, which is
    /// what somebody following a small board wants and a disaster on a large one.
    /// </summary>
    public List<string>? Keywords { get; set; }

    /// <summary>
    /// How many list pages to walk per check. One page is what a board that gets a few threads a
    /// day needs; the first check of a busy board is the only time more is useful.
    /// </summary>
    public int MaxPages { get; set; } = 1;
}
