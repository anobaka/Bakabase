using System;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Services;
using Bootstrap.Components.Orm;

namespace Bakabase.InsideWorld.Business.Components.Tag;

public class TagResourceMappingService(IServiceProvider serviceProvider)
    : FullMemoryCacheResourceService<InsideWorldDbContext, TagResourceMapping, int>(serviceProvider), ITagResourceMappingService
{

}