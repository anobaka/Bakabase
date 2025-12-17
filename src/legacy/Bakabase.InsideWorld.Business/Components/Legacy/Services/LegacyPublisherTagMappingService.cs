using System;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bootstrap.Components.Orm;

namespace Bakabase.InsideWorld.Business.Components.Legacy.Services
{
    [Obsolete]
    public class LegacyPublisherTagMappingService(IServiceProvider serviceProvider)
        : FullMemoryCacheResourceService<BakabaseDbContext,
            PublisherTagMapping, int>(serviceProvider);
}