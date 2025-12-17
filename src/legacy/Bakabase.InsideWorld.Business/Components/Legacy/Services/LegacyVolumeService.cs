using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Extensions;
using Bakabase.InsideWorld.Models.Models.Aos;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bootstrap.Components.Orm;

namespace Bakabase.InsideWorld.Business.Components.Legacy.Services
{
    [Obsolete]
    public class LegacyVolumeService(IServiceProvider serviceProvider)
        : FullMemoryCacheResourceService<BakabaseDbContext, Volume, int>(serviceProvider)
    {
        // public async Task<Dictionary<int, VolumeDto>> GetByResourceKeys(List<int> resourceIds)
        // {
        //     var volumes = (await GetAll(a => resourceIds.Contains(a.ResourceId)))
        //         .ToDictionary(a => a.ResourceId, a => a.ToDto());
        //     return resourceIds.ToDictionary(a => a, a => volumes.TryGetValue(a, out var v) ? v : null);
        // }
        //
        // [Obsolete($"Use {nameof(PutRange)} instead")]
        // public new async Task<VolumeRangeAddResult> AddRange(List<Volume> volumes)
        // {
        //     var volumesMap = volumes.ToDictionary(a => a.ResourceId, a => a);
        //     var resourceIds = volumesMap.Keys.ToList();
        //     var exists = (await base.GetByKeys(resourceIds)).ToDictionary(a => a.ResourceId, a => a);
        //     var existIds = exists.Values.Select(a => a.ResourceId).ToHashSet();
        //     var toBeAdded = volumes.Where(a => !existIds.Contains(a.ResourceId)).ToList();
        //     var @new = await base.AddRange(toBeAdded);
        //     var toBeUpdated = volumes.Where(v => exists.TryGetValue(v.ResourceId, out var e) && !e.Equals(v))
        //         .ToDictionary(a => a.ResourceId, a => a);
        //     await base.UpdateRange(toBeUpdated.Values);
        //     var data = exists.Select(e => toBeUpdated.TryGetValue(e.Key, out var v) ? v : e.Value).Concat(@new.Data)
        //         .ToList();
        //     return new VolumeRangeAddResult
        //     {
        //         AddedCount = @new.Data.Count,
        //         Data = data,
        //         ExistingCount = exists.Count
        //     };
        // }
        //
        // public async Task<VolumeRangeAddResult> PutRange(Dictionary<int, Volume?> volumesMap)
        // {
        //     var invalidResourceIds = volumesMap.Where(x => x.Value == null).Select(x => x.Key).ToArray();
        //
        //     var validVolumesMap = volumesMap.Where(x => x.Value != null).ToDictionary(x => x.Key, x => x.Value!);
        //     var volumes = validVolumesMap.Values.ToList();
        //     var resourceIds = validVolumesMap.Keys.ToList();
        //     var exists = (await base.GetByKeys(resourceIds)).ToDictionary(a => a.ResourceId, a => a);
        //     var existIds = exists.Values.Select(a => a.ResourceId).ToHashSet();
        //     var toBeAdded = volumes.Where(a => !existIds.Contains(a.ResourceId)).ToList();
        //     var toBeUpdated = volumes.Where(v => exists.TryGetValue(v.ResourceId, out var e) && !e.Equals(v))
        //         .ToDictionary(a => a.ResourceId, a => a);
        //
        //     var @new = await base.AddRange(toBeAdded!);
        //     await base.RemoveByKeys(invalidResourceIds);
        //     await base.UpdateRange(toBeUpdated.Values);
        //
        //     var data = exists.Select(e => toBeUpdated.TryGetValue(e.Key, out var v) ? v : e.Value).Concat(@new.Data)
        //         .ToList();
        //     return new VolumeRangeAddResult
        //     {
        //         AddedCount = @new.Data.Count,
        //         Data = data,
        //         ExistingCount = exists.Count
        //     };
        // }
    }
}