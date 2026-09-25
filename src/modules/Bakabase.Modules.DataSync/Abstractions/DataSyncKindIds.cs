namespace Bakabase.Modules.DataSync.Abstractions;

public static class DataSyncKindIds
{
    public const string ExtensionGroup = "extensionGroup";
    public const string CustomProperty = "customProperty";

    /// <summary>Apply order (topological by DependsOn, ties ordinal). Shipped to the frontend as DataSyncKinds.</summary>
    public static readonly IReadOnlyList<string> All = [ExtensionGroup, CustomProperty];
}

/// <summary>
/// Kind ids match ^[a-z][A-Za-z0-9]{1,63}$ and are never renamed.
/// <list type="bullet">
/// <item><c>customProperty</c>: HasOrder, HasChildren, SupportsChildrenLocal = true, true, true; ChildNoun
/// "option". SupportsChildrenLocal is effectively true only for the four reference types.</item>
/// <item><c>extensionGroup</c>: false, true, false; ChildNoun "extension".</item>
/// </list>
/// </summary>
public sealed record DataSyncKindDescriptor(
    string Kind,
    int SchemaVersion,                      // current content schema this build writes
    IReadOnlyList<string> DependsOn,        // kinds that must be applied first (both empty today)
    Type ContentType,                       // canonical DTO type (guardrail tests reflect over it)
    bool AutoLinkIdentical,                 // extensionGroup: true (D09 exception), customProperty: false
    bool HasOrder,                          // the kind's entities carry an orderKey (§3.7)
    bool HasChildren,
    bool SupportsChildrenLocal,             // offers "Sync the definition only" (§3.6)
    string ChildNoun);                      // used in UI keys only

/// <summary>
/// Clash: same name ignoring case/trim, different subtype. Similar: same name ignoring case/trim, same subtype.
/// Exact: ordinal-equal name, same subtype. Identical: Exact and equal canonical content except keys and child ids.
/// </summary>
public enum DataSyncNaturalMatch { None = 0, Clash = 1, Similar = 2, Exact = 3, Identical = 4 }
