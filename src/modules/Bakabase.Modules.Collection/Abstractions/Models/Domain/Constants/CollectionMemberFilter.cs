namespace Bakabase.Modules.Collection.Abstractions.Models.Domain.Constants;

/// <summary>
/// Which members to show. These are the questions a user actually asks of a collection: what is
/// missing, what is on its way, what did I decide not to want.
/// </summary>
public enum CollectionMemberFilter
{
    All = 0,

    /// <summary>Members with local files.</summary>
    Owned = 1,

    /// <summary>Members without local files and with nothing getting them.</summary>
    Missing = 2,

    /// <summary>Members something is currently getting.</summary>
    Acquiring = 3,

    Ignored = 4,

    /// <summary>Brought in by a source that has since stopped listing them.</summary>
    GoneFromSource = 5
}
