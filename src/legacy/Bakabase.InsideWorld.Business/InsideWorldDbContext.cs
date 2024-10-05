﻿using System;
using Bakabase.Abstractions.Models.Db;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models;
using Bakabase.InsideWorld.Business.Components.Legacy.Models;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bakabase.Modules.Property.Abstractions.Models.Db;
using Bootstrap.Components.Logging.LogService.Models.Entities;
using Microsoft.EntityFrameworkCore;
using EnhancementRecord = Bakabase.Abstractions.Models.Db.EnhancementRecord;
using LegacyAlias = Bakabase.InsideWorld.Models.Models.Entities.LegacyAlias;
using Tag = Bakabase.InsideWorld.Models.Models.Entities.Tag;

namespace Bakabase.InsideWorld.Business
{
    public class InsideWorldDbContext : DbContext
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
        public DbSet<Category> ResourceCategories { get; set; }

        public DbSet<MediaLibrary> MediaLibraries { get; set; }
        public DbSet<Playlist> Playlists { get; set; }
        public DbSet<ComponentOptions> ComponentOptions { get; set; }
        public DbSet<CategoryComponent> CategoryComponents { get; set; }

        public DbSet<DownloadTask> DownloadTasks { get; set; }

        public DbSet<Password> Passwords { get; set; }

        public DbSet<BulkModification> BulkModifications { get; set; }
        public DbSet<BulkModificationDiff> BulkModificationDiffs { get; set; }
        public DbSet<BulkModificationTempData> BulkModificationTempData { get; set; }

        public DbSet<CustomPropertyDbModel> CustomProperties { get; set; }
        public DbSet<CustomPropertyValueDbModel> CustomPropertyValues { get; set; }
        public DbSet<CategoryCustomPropertyMapping> CategoryCustomPropertyMappings { get; set; }

        public DbSet<Enhancement> Enhancements { get; set; }
        public DbSet<CategoryEnhancerOptions> CategoryEnhancerOptions { get; set; }
        public DbSet<EnhancementRecord> EnhancementRecords { get; set; }

        public DbSet<Resource> ResourcesV2 { get; set; }
        public DbSet<ReservedPropertyValue> ReservedPropertyValues { get; set; }
        public DbSet<Modules.Alias.Abstractions.Models.Db.Alias> AliasesV2 { get; set; }

        public InsideWorldDbContext()
        {
        }

        public InsideWorldDbContext(DbContextOptions<InsideWorldDbContext> options) : base(options)
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

            modelBuilder.Entity<MediaLibrary>(t =>
            {
                t.HasIndex(a => a.CategoryId);
                t.HasIndex(a => a.Name);
            });

            modelBuilder.Entity<DownloadTask>(t =>
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

            modelBuilder.Entity<Resource>(r => { r.HasIndex(x => x.Path).IsUnique(); });

            modelBuilder.Entity<EnhancementRecord>(er =>
            {
                er.HasIndex(x => x.EnhancerId);
                er.HasIndex(x => x.ResourceId);
                er.HasIndex(x => new {x.EnhancerId, x.ResourceId}).IsUnique();
            });
        }
    }
}