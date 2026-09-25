// Identity/: the enum lives in the module's root namespace so every DataSync namespace sees it (§2.1).
namespace Bakabase.Modules.DataSync;

/// <summary>Relation of a version vector A to another vector B (<c>A.CompareTo(B)</c>).</summary>
public enum DataSyncVvRelation { Equal = 1, Dominates = 2, DominatedBy = 3, Concurrent = 4 }
