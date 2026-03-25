using System;
using Bakabase.Abstractions.Models.Db;
using Bakabase.InsideWorld.Business.Components.Downloader.Models.Db;
using Bakabase.InsideWorld.Business.Components.PlayList.Models.Db;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Db;
using Bakabase.InsideWorld.Business.Models.Db;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bakabase.Modules.BulkModification.Components;
using Bakabase.Modules.BulkModification.Models.Db;
using Bakabase.Modules.Comparison.Components;
using Bakabase.Modules.AI.Models.Db;
using Bakabase.Modules.Comparison.Models.Db;
using Bakabase.Modules.Property.Abstractions.Models.Db;
using Microsoft.EntityFrameworkCore;
using EnhancementRecord = Bakabase.Abstractions.Models.Db.EnhancementRecord;
using ReservedPropertyValue = Bakabase.Abstractions.Models.Db.ReservedPropertyValue;
using SpecialText = Bakabase.Abstractions.Models.Db.SpecialText;

namespace Bakabase.InsideWorld.Business
{
    public class BakabaseDbContext : DbContext, IBulkModificationDbContext, IComparisonDbContext
    {
        public DbSet<SpecialText> SpecialTexts { get; set; }
        public DbSet<PlayListDbModel> Playlists { get; set; }

        public DbSet<DownloadTaskDbModel> DownloadTasks { get; set; }

        public DbSet<Password> Passwords { get; set; }

        public DbSet<BulkModificationDbModel> BulkModifications { get; set; }
        public DbSet<BulkModificationDiffDbModel> BulkModificationDiffs { get; set; }

        public DbSet<CustomPropertyDbModel> CustomProperties { get; set; }
        public DbSet<CustomPropertyValueDbModel> CustomPropertyValues { get; set; }

        public DbSet<EnhancementDbModel> Enhancements { get; set; }
        public DbSet<EnhancementRecord> EnhancementRecords { get; set; }

        public DbSet<ResourceDbModel> ResourcesV2 { get; set; }
        public DbSet<ReservedPropertyValue> ReservedPropertyValues { get; set; }
        public DbSet<Modules.Alias.Abstractions.Models.Db.Alias> AliasesV2 { get; set; }
        public DbSet<ResourceCacheDbModel> ResourceCaches { get; set; }

        public DbSet<PlayHistoryDbModel> PlayHistories { get; set; }
        public DbSet<ThirdPartyContentTrackerDbModel> ThirdPartyContentTrackers { get; set; }

        public DbSet<ExtensionGroupDbModel> ExtensionGroups { get; set; }
        public DbSet<MediaLibraryTemplateDbModel> MediaLibraryTemplates { get; set; }
        public DbSet<MediaLibraryV2DbModel> MediaLibrariesV2 { get; set; }

        public DbSet<PostParserTaskDbModel> PostParserTasks { get; set; }

        // New tables for media library refactoring
        public DbSet<PathMarkDbModel> PathMarks { get; set; }
        public DbSet<MediaLibraryResourceMappingDbModel> MediaLibraryResourceMappings { get; set; }
        public DbSet<ResourceProfileDbModel> ResourceProfiles { get; set; }

        // PathMark effect tracking tables
        public DbSet<ResourceMarkEffectDbModel> ResourceMarkEffects { get; set; }
        public DbSet<PropertyMarkEffectDbModel> PropertyMarkEffects { get; set; }

        // AI module tables
        public DbSet<LlmProviderConfigDbModel> LlmProviderConfigs { get; set; }
        public DbSet<LlmUsageLogDbModel> LlmUsageLogs { get; set; }
        public DbSet<LlmCallCacheEntryDbModel> LlmCallCacheEntries { get; set; }
        public DbSet<AiFeatureConfigDbModel> AiFeatureConfigs { get; set; }

        // Resource source tables
        public DbSet<ResourceSourceLinkDbModel> ResourceSourceLinks { get; set; }
        public DbSet<SourceMetadataMappingDbModel> SourceMetadataMappings { get; set; }
        public DbSet<SteamAppDbModel> SteamApps { get; set; }
        public DbSet<DLsiteWorkDbModel> DLsiteWorks { get; set; }
        public DbSet<ExHentaiGalleryDbModel> ExHentaiGalleries { get; set; }

        // Comparison module tables
        public DbSet<ComparisonPlanDbModel> ComparisonPlans { get; set; }
        public DbSet<ComparisonRuleDbModel> ComparisonRules { get; set; }
        public DbSet<ComparisonResultGroupDbModel> ComparisonResultGroups { get; set; }
        public DbSet<ComparisonResultGroupMemberDbModel> ComparisonResultGroupMembers { get; set; }
        public DbSet<ComparisonResultPairDbModel> ComparisonResultPairs { get; set; }

        public BakabaseDbContext()
        {
        }

        public BakabaseDbContext(DbContextOptions<BakabaseDbContext> options) : base(options)
        {
            Database.OpenConnection();
            // cache_size is working with current connection only.
            Database.ExecuteSqlRaw($"PRAGMA cache_size = {5_000_000}");
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            base.OnConfiguring(optionsBuilder);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<DownloadTaskDbModel>(t =>
            {
                t.HasIndex(a => a.ThirdPartyId);
                t.HasIndex(a => new {a.ThirdPartyId, a.Type});
                t.HasIndex(a => a.Status);
            });

            modelBuilder.Entity<Password>(t =>
            {
                t.HasIndex(a => a.LastUsedAt);
                t.HasIndex(a => a.UsedTimes);
            });

            modelBuilder.Entity<CustomPropertyDbModel>(t => { });

            modelBuilder.Entity<CustomPropertyValueDbModel>(t =>
            {
                t.HasIndex(x => new {x.ResourceId});
                t.HasIndex(x => x.PropertyId);
                t.HasIndex(x => new {x.ResourceId, x.PropertyId, x.Scope}).IsUnique();
            });

            modelBuilder.Entity<ReservedPropertyValue>(t =>
            {
                t.HasIndex(x => new {x.ResourceId, x.Scope}).IsUnique();
            });

            modelBuilder.Entity<ResourceDbModel>(r =>
            {
                r.HasIndex(x => x.Path);
                r.HasIndex(x => x.Status);
            });

            modelBuilder.Entity<EnhancementRecord>(er =>
            {
                er.HasIndex(x => x.EnhancerId);
                er.HasIndex(x => x.ResourceId);
                er.HasIndex(x => new {x.EnhancerId, x.ResourceId}).IsUnique();
            });

            modelBuilder.Entity<BulkModificationDbModel>(bm => { });

            modelBuilder.Entity<BulkModificationDiffDbModel>(bmd =>
            {
                bmd.HasIndex(x => new {x.BulkModificationId, x.ResourceId}).IsUnique();
            });

            modelBuilder.Entity<PlayHistoryDbModel>(a =>
            {
                a.HasIndex(x => x.ResourceId);
                a.HasIndex(x => x.PlayedAt);
            });

            modelBuilder.Entity<ThirdPartyContentTrackerDbModel>(t =>
            {
                t.HasIndex(x => new { x.DomainKey, x.Filter, x.ContentId }).IsUnique();
                t.HasIndex(x => x.DomainKey);
                t.HasIndex(x => x.ViewedAt);
            });

            modelBuilder.Entity<MediaLibraryV2DbModel>(t =>
            {
                // t.Property(x => x.Color); // Optional: add for clarity
            });

            // New tables for media library refactoring
            modelBuilder.Entity<MediaLibraryResourceMappingDbModel>(t =>
            {
                t.HasIndex(x => x.MediaLibraryId);
                t.HasIndex(x => x.ResourceId);
                t.HasIndex(x => new { x.MediaLibraryId, x.ResourceId }).IsUnique();
            });

            modelBuilder.Entity<ResourceProfileDbModel>(t =>
            {
                t.HasIndex(x => x.Priority);
            });

            modelBuilder.Entity<PathMarkDbModel>(t =>
            {
                t.HasIndex(x => x.Path);
                t.HasIndex(x => x.SyncStatus);
                t.HasIndex(x => x.IsDeleted);
                t.HasIndex(x => new { x.Path, x.Type, x.Priority });
            });

            // PathMark effect tracking tables
            modelBuilder.Entity<ResourceMarkEffectDbModel>(t =>
            {
                t.HasIndex(x => x.MarkId);
                t.HasIndex(x => x.Path);
                t.HasIndex(x => new { x.MarkId, x.Path }).IsUnique();
            });

            modelBuilder.Entity<PropertyMarkEffectDbModel>(t =>
            {
                t.HasIndex(x => x.MarkId);
                t.HasIndex(x => new { x.PropertyPool, x.PropertyId, x.ResourceId });
                t.HasIndex(x => new { x.MarkId, x.PropertyPool, x.PropertyId, x.ResourceId }).IsUnique();
            });

            // AI module tables
            modelBuilder.Entity<LlmProviderConfigDbModel>(t =>
            {
                t.HasIndex(x => x.ProviderType);
                t.HasIndex(x => x.IsEnabled);
            });

            modelBuilder.Entity<LlmUsageLogDbModel>(t =>
            {
                t.HasIndex(x => x.ProviderConfigId);
                t.HasIndex(x => x.CreatedAt);
                t.HasIndex(x => x.Feature);
                t.HasIndex(x => x.CacheHit);
            });

            modelBuilder.Entity<LlmCallCacheEntryDbModel>(t =>
            {
                t.HasIndex(x => x.CacheKey).IsUnique();
                t.HasIndex(x => x.ExpiresAt);
            });

            modelBuilder.Entity<AiFeatureConfigDbModel>(t =>
            {
                t.HasIndex(x => x.Feature).IsUnique();
            });

            // Resource source link table
            modelBuilder.Entity<ResourceSourceLinkDbModel>(t =>
            {
                t.HasIndex(x => x.ResourceId);
                t.HasIndex(x => new { x.Source, x.SourceKey });
                t.HasIndex(x => new { x.ResourceId, x.Source, x.SourceKey }).IsUnique();
            });

            // Resource source tables
            modelBuilder.Entity<SteamAppDbModel>(t =>
            {
                t.HasIndex(x => x.AppId).IsUnique();
                t.HasIndex(x => x.ResourceId);
            });

            modelBuilder.Entity<DLsiteWorkDbModel>(t =>
            {
                t.HasIndex(x => x.WorkId).IsUnique();
                t.HasIndex(x => x.ResourceId);
            });

            modelBuilder.Entity<ExHentaiGalleryDbModel>(t =>
            {
                t.HasIndex(x => new { x.GalleryId, x.GalleryToken }).IsUnique();
                t.HasIndex(x => x.ResourceId);
            });

            // Comparison module tables
            modelBuilder.Entity<ComparisonPlanDbModel>(t =>
            {
                t.HasIndex(x => x.Name);
                t.HasIndex(x => x.CreatedAt);
            });

            modelBuilder.Entity<ComparisonRuleDbModel>(t =>
            {
                t.HasIndex(x => x.PlanId);
                t.HasIndex(x => new { x.PlanId, x.Order });
            });

            modelBuilder.Entity<ComparisonResultGroupDbModel>(t =>
            {
                t.HasIndex(x => x.PlanId);
                t.HasIndex(x => x.MemberCount);
                t.HasIndex(x => new { x.PlanId, x.MemberCount });
            });

            modelBuilder.Entity<ComparisonResultGroupMemberDbModel>(t =>
            {
                t.HasIndex(x => x.GroupId);
                t.HasIndex(x => x.ResourceId);
                t.HasIndex(x => new { x.GroupId, x.ResourceId }).IsUnique();
            });

            modelBuilder.Entity<ComparisonResultPairDbModel>(t =>
            {
                t.HasIndex(x => x.GroupId);
                t.HasIndex(x => new { x.Resource1Id, x.Resource2Id });
                t.HasIndex(x => new { x.GroupId, x.Resource1Id, x.Resource2Id }).IsUnique();
            });
        }
    }
}