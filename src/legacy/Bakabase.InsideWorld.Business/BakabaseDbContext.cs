using System;
using Bakabase.Abstractions.Models.Db;
using Bakabase.InsideWorld.Business.Components.Downloader.Models.Db;
using Bakabase.InsideWorld.Business.Components.Legacy.Models;
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
using Bootstrap.Components.Logging.LogService.Models.Entities;
using Microsoft.EntityFrameworkCore;
using CategoryDbModel = Bakabase.Abstractions.Models.Db.CategoryDbModel;
using CategoryEnhancerOptions = Bakabase.Abstractions.Models.Db.CategoryEnhancerOptions;
using EnhancementRecord = Bakabase.Abstractions.Models.Db.EnhancementRecord;
using LegacyAlias = Bakabase.InsideWorld.Models.Models.Entities.LegacyAlias;
using MediaLibraryDbModel = Bakabase.Abstractions.Models.Db.MediaLibraryDbModel;
using ReservedPropertyValue = Bakabase.Abstractions.Models.Db.ReservedPropertyValue;
using SpecialText = Bakabase.Abstractions.Models.Db.SpecialText;
using Tag = Bakabase.InsideWorld.Models.Models.Entities.Tag;

namespace Bakabase.InsideWorld.Business
{
    public class BakabaseDbContext : DbContext, IBulkModificationDbContext, IComparisonDbContext
    {
        [Obsolete] public DbSet<LegacyAlias> Aliases { get; set; }
        [Obsolete] public DbSet<AliasGroup> AliasGroups { get; set; }
        [Obsolete] public DbSet<LegacyDbResource> Resources { get; set; }
        [Obsolete] public DbSet<Original> Originals { get; set; }
        [Obsolete] public DbSet<Publisher> Publishers { get; set; }
        [Obsolete] public DbSet<Series> Series { get; set; }
        [Obsolete] public DbSet<CustomResourceProperty> CustomResourceProperties { get; set; }
        [Obsolete] public DbSet<PublisherResourceMapping> OrganizationResourceMappings { get; set; }
        [Obsolete] public DbSet<OriginalResourceMapping> OriginalResourceMappings { get; set; }
        [Obsolete] public DbSet<ResourceTagMapping> ResourceTagMappings { get; set; }
        [Obsolete] public DbSet<PublisherTagMapping> PublisherTagMappings { get; set; }
        [Obsolete] public DbSet<Favorites> Favorites { get; set; }
        [Obsolete] public DbSet<FavoritesResourceMapping> FavoritesResourceMappings { get; set; }
        [Obsolete] public DbSet<Volume> Volumes { get; set; }
        [Obsolete] public DbSet<Tag> Tags { get; set; }
        [Obsolete] public DbSet<TagGroup> TagGroups { get; set; }
        [Obsolete] public DbSet<Log> Logs { get; set; }
        [Obsolete] public DbSet<CustomPlayerOptions> CustomPlayerOptionsList { get; set; }
        [Obsolete] public DbSet<CustomPlayableFileSelectorOptions> CustomPlayableFileSelectorOptionsList { get; set; }

        public DbSet<SpecialText> SpecialTexts { get; set; }
        public DbSet<CategoryDbModel> ResourceCategories { get; set; }

        public DbSet<MediaLibraryDbModel> MediaLibraries { get; set; }
        public DbSet<PlayListDbModel> Playlists { get; set; }
        public DbSet<ComponentOptions> ComponentOptions { get; set; }
        public DbSet<CategoryComponent> CategoryComponents { get; set; }

        public DbSet<DownloadTaskDbModel> DownloadTasks { get; set; }

        public DbSet<Password> Passwords { get; set; }

        public DbSet<BulkModificationDbModel> BulkModifications { get; set; }
        public DbSet<BulkModificationDiffDbModel> BulkModificationDiffs { get; set; }

        public DbSet<CustomPropertyDbModel> CustomProperties { get; set; }
        public DbSet<CustomPropertyValueDbModel> CustomPropertyValues { get; set; }
        public DbSet<CategoryCustomPropertyMapping> CategoryCustomPropertyMappings { get; set; }

        public DbSet<EnhancementDbModel> Enhancements { get; set; }
        public DbSet<CategoryEnhancerOptions> CategoryEnhancerOptions { get; set; }
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

            modelBuilder.Entity<LegacyAlias>(t =>
            {
                t.HasIndex(a => a.GroupId).IsUnique(false);
                t.HasIndex(a => a.IsPreferred).IsUnique(false);
                t.HasIndex(a => a.Name).IsUnique();
            });

            modelBuilder.Entity<PublisherResourceMapping>(t =>
            {
                t.HasIndex(t1 => new {t1.PublisherId, t1.ResourceId, t1.ParentPublisherId}).IsUnique();
            });

            modelBuilder.Entity<OriginalResourceMapping>(t =>
            {
                t.HasIndex(t1 => new {t1.OriginalId, t1.ResourceId}).IsUnique();
            });

            modelBuilder.Entity<PublisherTagMapping>(t =>
            {
                t.HasIndex(t1 => new {t1.TagId, t1.PublisherId}).IsUnique();
            });

            modelBuilder.Entity<ResourceTagMapping>(t =>
            {
                t.HasIndex(t1 => new {t1.TagId, t1.ResourceId}).IsUnique();
            });

            modelBuilder.Entity<TagGroup>(a => { a.HasIndex(b => b.Name).IsUnique(); });


            modelBuilder.Entity<LegacyDbResource>(t =>
            {
                t.HasIndex(a => a.CategoryId);
                t.HasIndex(a => a.Name);
                t.HasIndex(a => a.RawName);
                t.HasIndex(a => a.Language);
                t.HasIndex(a => a.CreateDt);
                t.HasIndex(a => a.UpdateDt);
                t.HasIndex(a => a.FileCreateDt);
                t.HasIndex(a => a.FileModifyDt);
                t.HasIndex(a => a.Rate);
            });

            modelBuilder.Entity<Original>(t => { t.HasIndex(a => a.Name); });
            modelBuilder.Entity<Publisher>(t => { t.HasIndex(a => a.Name); });
            modelBuilder.Entity<Series>(t => { t.HasIndex(a => a.Name); });
            modelBuilder.Entity<Volume>(t =>
            {
                t.HasIndex(a => a.Name);
                t.HasIndex(a => a.Title);
                t.HasIndex(a => a.ResourceId);
                t.HasIndex(a => a.SerialId);
            });

            modelBuilder.Entity<Tag>(t =>
            {
                t.HasIndex(a => a.Name);
                t.HasIndex(a => new {a.Name, a.GroupId}).IsUnique();
            });

            modelBuilder.Entity<MediaLibraryDbModel>(t =>
            {
                t.HasIndex(a => a.CategoryId);
                t.HasIndex(a => a.Name);
            });

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

            modelBuilder.Entity<CategoryCustomPropertyMapping>(t =>
            {
                t.HasIndex(x => new {x.CategoryId, x.PropertyId}).IsUnique();
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