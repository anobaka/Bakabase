using System;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Legacy.Models;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bakabase.InsideWorld.Models.RequestModels;
using Bootstrap.Components.Orm.Infrastructures;

namespace Bakabase.InsideWorld.Business.Components.Legacy.Services;

[Obsolete]
public class LegacyResourceService(IServiceProvider serviceProvider)
    : ResourceService<BakabaseDbContext, LegacyDbResource, int>(serviceProvider)
{
    // public async Task BatchUpdateTags(ResourceTagUpdateRequestModel model)
    // {
    //     await _resourceTagMappingService.PutRange(model.ResourceTagIds);
    //     await RunBatchSaveNfoBackgroundTask(model.ResourceTagIds.Keys.ToArray(),
    //         $"{nameof(ResourceService)}:BatchUpdateTags", true);
    // }
}