﻿// <auto-generated />
using System;
using Bakabase.InsideWorld.Business;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

#nullable disable

namespace Bakabase.InsideWorld.Business.Migrations
{
    [DbContext(typeof(InsideWorldDbContext))]
    [Migration("20230703095633_V171P3")]
    partial class V171P3
    {
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder.HasAnnotation("ProductVersion", "6.0.6");

            modelBuilder.Entity("Bakabase.InsideWorld.Models.Models.Entities.Alias", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.Property<int>("GroupId")
                        .HasColumnType("INTEGER");

                    b.Property<bool>("IsPreferred")
                        .HasColumnType("INTEGER");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasMaxLength(256)
                        .HasColumnType("TEXT");

                    b.HasKey("Id");

                    b.HasIndex("GroupId");

                    b.HasIndex("IsPreferred");

                    b.HasIndex("Name")
                        .IsUnique();

                    b.ToTable("Aliases");
                });

            modelBuilder.Entity("Bakabase.InsideWorld.Models.Models.Entities.AliasGroup", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.HasKey("Id");

                    b.ToTable("AliasGroups");
                });

            modelBuilder.Entity("Bakabase.InsideWorld.Models.Models.Entities.CategoryComponent", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.Property<int>("CategoryId")
                        .HasColumnType("INTEGER");

                    b.Property<string>("ComponentKey")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<int>("ComponentType")
                        .HasColumnType("INTEGER");

                    b.HasKey("Id");

                    b.ToTable("CategoryComponents");
                });

            modelBuilder.Entity("Bakabase.InsideWorld.Models.Models.Entities.ComponentOptions", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.Property<string>("ComponentAssemblyQualifiedTypeName")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<int>("ComponentType")
                        .HasColumnType("INTEGER");

                    b.Property<string>("Description")
                        .HasColumnType("TEXT");

                    b.Property<string>("Json")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.HasKey("Id");

                    b.ToTable("ComponentOptions");
                });

            modelBuilder.Entity("Bakabase.InsideWorld.Models.Models.Entities.CustomPlayableFileSelectorOptions", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.Property<string>("ExtensionsString")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<int>("MaxFileCount")
                        .HasColumnType("INTEGER");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.HasKey("Id");

                    b.ToTable("CustomPlayableFileSelectorOptionsList");
                });

            modelBuilder.Entity("Bakabase.InsideWorld.Models.Models.Entities.CustomPlayerOptions", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.Property<string>("CommandTemplate")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<string>("Executable")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<string>("SubCustomPlayerOptionsListJson")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.HasKey("Id");

                    b.ToTable("CustomPlayerOptionsList");
                });

            modelBuilder.Entity("Bakabase.InsideWorld.Models.Models.Entities.CustomResourceProperty", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.Property<int?>("Index")
                        .HasColumnType("INTEGER");

                    b.Property<string>("Key")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<int>("ResourceId")
                        .HasColumnType("INTEGER");

                    b.Property<string>("Value")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<int>("ValueType")
                        .HasColumnType("INTEGER");

                    b.HasKey("Id");

                    b.ToTable("CustomResourceProperties");
                });

            modelBuilder.Entity("Bakabase.InsideWorld.Models.Models.Entities.DownloadTask", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.Property<string>("Checkpoint")
                        .HasColumnType("TEXT");

                    b.Property<string>("DownloadPath")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<DateTime>("DownloadStatusUpdateDt")
                        .HasColumnType("TEXT");

                    b.Property<int?>("EndPage")
                        .HasColumnType("INTEGER");

                    b.Property<long?>("Interval")
                        .HasColumnType("INTEGER");

                    b.Property<string>("Key")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<string>("Message")
                        .HasColumnType("TEXT");

                    b.Property<string>("Name")
                        .HasColumnType("TEXT");

                    b.Property<decimal>("Progress")
                        .HasColumnType("TEXT");

                    b.Property<int?>("StartPage")
                        .HasColumnType("INTEGER");

                    b.Property<int>("Status")
                        .HasColumnType("INTEGER");

                    b.Property<int>("ThirdPartyId")
                        .HasColumnType("INTEGER");

                    b.Property<int>("Type")
                        .HasColumnType("INTEGER");

                    b.HasKey("Id");

                    b.HasIndex("Status");

                    b.HasIndex("ThirdPartyId");

                    b.HasIndex("ThirdPartyId", "Type");

                    b.ToTable("DownloadTasks");
                });

            modelBuilder.Entity("Bakabase.InsideWorld.Models.Models.Entities.EnhancementRecord", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.Property<DateTime>("CreateDt")
                        .HasColumnType("TEXT");

                    b.Property<string>("Enhancement")
                        .HasColumnType("TEXT");

                    b.Property<string>("EnhancerDescriptorId")
                        .HasColumnType("TEXT");

                    b.Property<string>("EnhancerName")
                        .HasColumnType("TEXT");

                    b.Property<string>("Message")
                        .HasColumnType("TEXT");

                    b.Property<int>("ResourceId")
                        .HasColumnType("INTEGER");

                    b.Property<string>("ResourceRawFullName")
                        .HasColumnType("TEXT");

                    b.Property<string>("RuleId")
                        .HasColumnType("TEXT");

                    b.Property<bool>("Success")
                        .HasColumnType("INTEGER");

                    b.HasKey("Id");

                    b.HasIndex("ResourceId");

                    b.ToTable("EnhancementRecords");
                });

            modelBuilder.Entity("Bakabase.InsideWorld.Models.Models.Entities.Favorites", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.Property<DateTime>("CreateDt")
                        .HasColumnType("TEXT");

                    b.Property<string>("Description")
                        .HasColumnType("TEXT");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.HasKey("Id");

                    b.ToTable("Favorites");
                });

            modelBuilder.Entity("Bakabase.InsideWorld.Models.Models.Entities.FavoritesResourceMapping", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.Property<int>("FavoritesId")
                        .HasColumnType("INTEGER");

                    b.Property<int>("ResourceId")
                        .HasColumnType("INTEGER");

                    b.HasKey("Id");

                    b.ToTable("FavoritesResourceMappings");
                });

            modelBuilder.Entity("Bakabase.InsideWorld.Models.Models.Entities.MediaLibrary", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.Property<int>("CategoryId")
                        .HasColumnType("INTEGER");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<int>("Order")
                        .HasColumnType("INTEGER");

                    b.Property<string>("PathConfigurationsJson")
                        .HasColumnType("TEXT");

                    b.Property<int>("ResourceCount")
                        .HasColumnType("INTEGER");

                    b.HasKey("Id");

                    b.HasIndex("CategoryId");

                    b.HasIndex("Name");

                    b.ToTable("MediaLibraries");
                });

            modelBuilder.Entity("Bakabase.InsideWorld.Models.Models.Entities.Original", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.HasKey("Id");

                    b.HasIndex("Name");

                    b.ToTable("Originals");
                });

            modelBuilder.Entity("Bakabase.InsideWorld.Models.Models.Entities.OriginalResourceMapping", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.Property<int>("OriginalId")
                        .HasColumnType("INTEGER");

                    b.Property<int>("ResourceId")
                        .HasColumnType("INTEGER");

                    b.HasKey("Id");

                    b.HasIndex("OriginalId", "ResourceId")
                        .IsUnique();

                    b.ToTable("OriginalResourceMappings");
                });

            modelBuilder.Entity("Bakabase.InsideWorld.Models.Models.Entities.Password", b =>
                {
                    b.Property<string>("Text")
                        .HasMaxLength(64)
                        .HasColumnType("TEXT");

                    b.Property<DateTime>("LastUsedAt")
                        .HasColumnType("TEXT");

                    b.Property<int>("UsedTimes")
                        .HasColumnType("INTEGER");

                    b.HasKey("Text");

                    b.HasIndex("LastUsedAt");

                    b.HasIndex("UsedTimes");

                    b.ToTable("Passwords");
                });

            modelBuilder.Entity("Bakabase.InsideWorld.Models.Models.Entities.Playlist", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.Property<int>("Interval")
                        .HasColumnType("INTEGER");

                    b.Property<string>("ItemsJson")
                        .HasColumnType("TEXT");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<int>("Order")
                        .HasColumnType("INTEGER");

                    b.HasKey("Id");

                    b.ToTable("Playlists");
                });

            modelBuilder.Entity("Bakabase.InsideWorld.Models.Models.Entities.Publisher", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.Property<bool>("Favorite")
                        .HasColumnType("INTEGER");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<int>("Rank")
                        .HasColumnType("INTEGER");

                    b.HasKey("Id");

                    b.HasIndex("Name");

                    b.ToTable("Publishers");
                });

            modelBuilder.Entity("Bakabase.InsideWorld.Models.Models.Entities.PublisherResourceMapping", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.Property<int?>("ParentPublisherId")
                        .HasColumnType("INTEGER");

                    b.Property<int>("PublisherId")
                        .HasColumnType("INTEGER");

                    b.Property<int>("ResourceId")
                        .HasColumnType("INTEGER");

                    b.HasKey("Id");

                    b.HasIndex("PublisherId", "ResourceId", "ParentPublisherId")
                        .IsUnique();

                    b.ToTable("OrganizationResourceMappings");
                });

            modelBuilder.Entity("Bakabase.InsideWorld.Models.Models.Entities.PublisherTagMapping", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.Property<int>("PublisherId")
                        .HasColumnType("INTEGER");

                    b.Property<int>("TagId")
                        .HasColumnType("INTEGER");

                    b.HasKey("Id");

                    b.HasIndex("TagId", "PublisherId")
                        .IsUnique();

                    b.ToTable("PublisherTagMappings");
                });

            modelBuilder.Entity("Bakabase.InsideWorld.Models.Models.Entities.Resource", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.Property<int>("CategoryId")
                        .HasColumnType("INTEGER");

                    b.Property<DateTime>("CreateDt")
                        .HasColumnType("TEXT");

                    b.Property<string>("Directory")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<DateTime>("FileCreateDt")
                        .HasColumnType("TEXT");

                    b.Property<DateTime>("FileModifyDt")
                        .HasColumnType("TEXT");

                    b.Property<bool>("HasChildren")
                        .HasColumnType("INTEGER");

                    b.Property<string>("Introduction")
                        .HasColumnType("TEXT");

                    b.Property<bool>("IsSingleFile")
                        .HasColumnType("INTEGER");

                    b.Property<int>("Language")
                        .HasColumnType("INTEGER");

                    b.Property<int>("MediaLibraryId")
                        .HasColumnType("INTEGER");

                    b.Property<string>("Name")
                        .HasColumnType("TEXT");

                    b.Property<int?>("ParentId")
                        .HasColumnType("INTEGER");

                    b.Property<decimal>("Rate")
                        .HasColumnType("TEXT");

                    b.Property<string>("RawName")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<DateTime?>("ReleaseDt")
                        .HasColumnType("TEXT");

                    b.Property<DateTime>("UpdateDt")
                        .HasColumnType("TEXT");

                    b.HasKey("Id");

                    b.HasIndex("CategoryId");

                    b.HasIndex("CreateDt");

                    b.HasIndex("FileCreateDt");

                    b.HasIndex("FileModifyDt");

                    b.HasIndex("Language");

                    b.HasIndex("Name");

                    b.HasIndex("Rate");

                    b.HasIndex("RawName");

                    b.HasIndex("UpdateDt");

                    b.ToTable("Resources");
                });

            modelBuilder.Entity("Bakabase.InsideWorld.Models.Models.Entities.ResourceCategory", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.Property<string>("Color")
                        .HasColumnType("TEXT");

                    b.Property<string>("ComponentsJsonData")
                        .HasColumnType("TEXT");

                    b.Property<int>("CoverSelectionOrder")
                        .HasColumnType("INTEGER");

                    b.Property<DateTime>("CreateDt")
                        .HasColumnType("TEXT");

                    b.Property<string>("EnhancementOptionsJson")
                        .HasColumnType("TEXT");

                    b.Property<bool>("GenerateNfo")
                        .HasColumnType("INTEGER");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<int>("Order")
                        .HasColumnType("INTEGER");

                    b.Property<bool>("TryCompressedFilesOnCoverSelection")
                        .HasColumnType("INTEGER");

                    b.HasKey("Id");

                    b.ToTable("ResourceCategories");
                });

            modelBuilder.Entity("Bakabase.InsideWorld.Models.Models.Entities.ResourceTagMapping", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.Property<int>("ResourceId")
                        .HasColumnType("INTEGER");

                    b.Property<int>("TagId")
                        .HasColumnType("INTEGER");

                    b.HasKey("Id");

                    b.HasIndex("TagId", "ResourceId")
                        .IsUnique();

                    b.ToTable("ResourceTagMappings");
                });

            modelBuilder.Entity("Bakabase.InsideWorld.Models.Models.Entities.Series", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.HasKey("Id");

                    b.HasIndex("Name");

                    b.ToTable("Series");
                });

            modelBuilder.Entity("Bakabase.InsideWorld.Models.Models.Entities.SpecialText", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.Property<int>("Type")
                        .HasColumnType("INTEGER");

                    b.Property<string>("Value1")
                        .IsRequired()
                        .HasMaxLength(64)
                        .HasColumnType("TEXT");

                    b.Property<string>("Value2")
                        .HasMaxLength(64)
                        .HasColumnType("TEXT");

                    b.HasKey("Id");

                    b.ToTable("SpecialTexts");
                });

            modelBuilder.Entity("Bakabase.InsideWorld.Models.Models.Entities.Tag", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.Property<string>("Color")
                        .HasColumnType("TEXT");

                    b.Property<int>("GroupId")
                        .HasColumnType("INTEGER");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<int>("Order")
                        .HasColumnType("INTEGER");

                    b.Property<int>("Source")
                        .HasColumnType("INTEGER");

                    b.HasKey("Id");

                    b.HasIndex("Name");

                    b.HasIndex("Name", "GroupId")
                        .IsUnique();

                    b.ToTable("Tags");
                });

            modelBuilder.Entity("Bakabase.InsideWorld.Models.Models.Entities.TagGroup", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<int>("Order")
                        .HasColumnType("INTEGER");

                    b.HasKey("Id");

                    b.HasIndex("Name")
                        .IsUnique();

                    b.ToTable("TagGroups");
                });

            modelBuilder.Entity("Bakabase.InsideWorld.Models.Models.Entities.Volume", b =>
                {
                    b.Property<int>("ResourceId")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.Property<int>("Index")
                        .HasColumnType("INTEGER");

                    b.Property<string>("Name")
                        .HasColumnType("TEXT");

                    b.Property<int>("SerialId")
                        .HasColumnType("INTEGER");

                    b.Property<string>("Title")
                        .HasColumnType("TEXT");

                    b.HasKey("ResourceId");

                    b.HasIndex("Name");

                    b.HasIndex("ResourceId");

                    b.HasIndex("SerialId");

                    b.HasIndex("Title");

                    b.ToTable("Volumes");
                });

            modelBuilder.Entity("Bootstrap.Components.Logging.LogService.Models.Entities.Log", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.Property<DateTime>("DateTime")
                        .HasColumnType("TEXT");

                    b.Property<string>("Event")
                        .HasColumnType("TEXT");

                    b.Property<int>("Level")
                        .HasColumnType("INTEGER");

                    b.Property<string>("Logger")
                        .HasColumnType("TEXT");

                    b.Property<string>("Message")
                        .HasColumnType("TEXT");

                    b.Property<bool>("Read")
                        .HasColumnType("INTEGER");

                    b.HasKey("Id");

                    b.ToTable("Logs");
                });
#pragma warning restore 612, 618
        }
    }
}
