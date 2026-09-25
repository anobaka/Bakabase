using System;
using System.Collections.Generic;
using System.Linq;
using Bakabase.Modules.DataSync.Abstractions;

namespace Bakabase.InsideWorld.Business.Components.DataSync;

/// <summary>
/// Every <c>DbSet</c> of <see cref="BakabaseDbContext"/>, classified for data sync (spec Appendix A; v3.1 Appendix A):
/// the two synced definition kinds, library data (never synced, D02) and everything else that never syncs, with why.
/// A new DbSet must be added here; <c>DbSetClassificationTests</c> fails until it is (see
/// <c>.claude/rules/data-sync.md</c>, "Adding a DbSet").
/// </summary>
public static class DataSyncDbSetClassification
{
    public const string SyncedReason = "Definitions";
    public const string LibraryDataReason = "Library data (never, D02)";
    public const string SyncStateReason = "Sync state (never)";

    public static IReadOnlyDictionary<string, DataSyncTableClassification> All { get; } = Build();

    public static DataSyncTableClassification? Get(string dbSetName) => All.GetValueOrDefault(dbSetName);

    private static Dictionary<string, DataSyncTableClassification> Build()
    {
        var all = new Dictionary<string, DataSyncTableClassification>(StringComparer.Ordinal);

        void Add(DataSyncTableClass @class, string reason, string? kind, params string[] dbSets)
        {
            foreach (var dbSet in dbSets)
                all.Add(dbSet, new DataSyncTableClassification(dbSet, @class, kind, reason));
        }

        Add(DataSyncTableClass.Synced, SyncedReason, DataSyncKindIds.CustomProperty, "CustomProperties");
        Add(DataSyncTableClass.Synced, SyncedReason, DataSyncKindIds.ExtensionGroup, "ExtensionGroups");

        Add(DataSyncTableClass.LibraryData, LibraryDataReason, null,
            "Playlists", "BulkModificationDiffs", "ResourceHealthScores", "CustomPropertyValues", "Enhancements",
            "EnhancementRecords", "ResourcesV2", "ReservedPropertyValues", "ResourceCaches", "PlayHistories",
            "PropertyValueScopePreferences", "ThirdPartyContentTrackers", "MediaLibraryResourceMappings",
            "ResourceMarkEffects", "PropertyMarkEffects", "ResourceMoveRecords", "ResourceSourceLinks",
            "ResourceExternalIdentities", "SteamApps", "DLsiteWorks", "ExHentaiGalleries", "DataCards",
            "DataCardPropertyValues", "ComparisonResultGroups", "ComparisonResultGroupMembers",
            "ComparisonResultPairs", "CollectionResourceMappings", "ResourceMatchSuggestions");

        Add(DataSyncTableClass.NeverSync, "Future kind (P1 vocabulary)", null, "AliasesV2", "TextTypes", "TextEntries");
        Add(DataSyncTableClass.NeverSync, "Future kind (P4)", null,
            "MediaLibrariesV2", "ResourceProfiles", "SourceMetadataMappings");
        Add(DataSyncTableClass.NeverSync, "Future kind (P5)", null,
            "HealthScoreProfiles", "DataCardTypes", "ComparisonPlans", "ComparisonRules", "Collections");
        Add(DataSyncTableClass.NeverSync, "Device paths (never)", null, "PathMarks");
        Add(DataSyncTableClass.NeverSync, "Secret (never)", null, "Passwords", "AiProviders");
        Add(DataSyncTableClass.NeverSync, "Active, would run on every device (never in MVP)", null,
            "Subscriptions", "WorkflowDefinitions", "WorkflowActivities", "AiFeatureConfigs", "LlmToolConfigs",
            "AigcGenerators", "AigcGeneratorPropertyPresets");
        Add(DataSyncTableClass.NeverSync, "Device task or runtime state", null,
            "DownloadTasks", "DownloadRecords", "DownloadResults", "DownloadResultOwners", "DownloadResultProcessing",
            "PostParserTasks", "AcquisitionLeads", "AcquisitionTasks", "BulkModifications", "WorkflowRuns",
            "SubscriptionSnapshots", "FileRenameEntries", "Notifications");
        Add(DataSyncTableClass.NeverSync, "Device logs and history", null,
            "LlmUsageLogs", "LlmCallCacheEntries", "ChatConversations", "ChatMessages", "AigcGenerationRuns",
            "AigcArtifacts");
        Add(DataSyncTableClass.NeverSync, "Deprecated or dormant", null, "SpecialTexts", "MediaLibraryTemplates");
        Add(DataSyncTableClass.NeverSync, SyncStateReason, null,
            "DataSyncEntities", "DataSyncKeyAliases", "DataSyncApplyLogs", "DataSyncLinks", "DataSyncPeerBases",
            "DataSyncInboxItems", "DataSyncLocalStates", "DataSyncReaders");

        return all;
    }

    /// <summary>The kinds the Synced entries claim.</summary>
    public static IReadOnlyList<string> SyncedKinds =>
        All.Values.Where(c => c.Class == DataSyncTableClass.Synced).Select(c => c.Kind!).ToList();
}
