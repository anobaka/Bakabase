using System;
using Bakabase.Abstractions.Models.Db;
using Bootstrap.Components.Orm;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.InsideWorld.Business.Services;

/// <summary>
/// Singleton index service for MediaLibraryResourceMapping.
/// Key1 = MediaLibraryId, Key2 = ResourceId
/// </summary>
public class MediaLibraryResourceMappingIndexService(IServiceProvider serviceProvider)
    : BidirectionalMappingIndexService<MediaLibraryResourceMappingDbModel, int, int>(
        m => m.MediaLibraryId,
        m => m.ResourceId,
        async () =>
        {
            using var scope = serviceProvider.CreateScope();
            var orm = scope.ServiceProvider
                .GetRequiredService<FullMemoryCacheResourceService<BakabaseDbContext, MediaLibraryResourceMappingDbModel, int>>();
            return await orm.GetAll();
        });
