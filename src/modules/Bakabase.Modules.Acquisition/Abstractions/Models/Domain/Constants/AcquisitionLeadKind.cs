namespace Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;

/// <summary>
/// How a resource can be obtained. Two families, and the difference matters:
/// <list type="bullet">
/// <item><see cref="PlatformHolding"/> is the platform the user holds the resource on — a bought
/// DLsite work, a Steam application, a favourited gallery. It is the resource's identity, stored as
/// a <c>ResourceSourceLink</c>, and is only ever <em>derived</em> into a lead, never stored as
/// one.</item>
/// <item>Everything else is a sharing channel: somewhere a link was shared. It says nothing about
/// what the resource is and nobody holds anything there, so it is stored as a lead and never as an
/// identity.</item>
/// </list>
/// </summary>
public enum AcquisitionLeadKind
{
    /// <summary>
    /// Derived from a <c>ResourceSourceLink</c> on a platform that holds the files. Never stored.
    /// </summary>
    PlatformHolding = 1,

    /// <summary>A page that shares links — a forum post, a tweet, a blog entry.</summary>
    SharedPage = 2,

    /// <summary>A document or spreadsheet of links, addressed down to the row that shares this one.</summary>
    SharedDocument = 3,

    /// <summary>A direct download URL.</summary>
    DirectUrl = 4,

    /// <summary>A magnet link.</summary>
    Magnet = 5,

    /// <summary>
    /// The user will obtain it by hand and does not want the system to try. Not stored as a lead —
    /// it describes a recipe's choice, not a place to get files from.
    /// </summary>
    Manual = 6
}
