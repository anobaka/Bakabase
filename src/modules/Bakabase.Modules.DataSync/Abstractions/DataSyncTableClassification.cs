namespace Bakabase.Modules.DataSync.Abstractions;

// v3.1 Appendix A, unchanged. The classification itself (every DbSet of BakabaseDbContext) lives in Business
// (DataSyncDbSetClassification); these are its contract types.

public enum DataSyncTableClass { Synced = 1, NeverSync = 2, LibraryData = 3 }

/// <param name="Kind">The data sync kind of a Synced table; null otherwise.</param>
public sealed record DataSyncTableClassification(string DbSetName, DataSyncTableClass Class, string? Kind,
    string Reason);
