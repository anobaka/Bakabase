using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bootstrap.Components.Orm;
using Bootstrap.Components.Orm.Infrastructures;

namespace Bakabase.InsideWorld.Business.Components.Legacy.Services
{
    [Obsolete]
    public class
        LegacyResourcePropertyService(IServiceProvider serviceProvider)
        : FullMemoryCacheResourceService<BakabaseDbContext, CustomResourceProperty,
            int>(serviceProvider)
    {
        // public async Task<int[]> SearchResourceIds(Dictionary<string, string> properties)
        // {
        //     var data = await base.GetAll(t => properties.ContainsKey(t.Key) && t.Value.Contains(properties[t.Key]));
        //     return data.Select(t => t.ResourceId).Distinct().ToArray();
        // }
        //
        // public async Task<int[]> SearchResourceIdsByEverything(string everything)
        // {
        //     var data = await base.GetAll(t => t.Value.Contains(everything));
        //     return data.Select(t => t.ResourceId).Distinct().ToArray();
        // }
        //
        // public async Task<string[]> GetAllKeys()
        // {
        //     var data = await base.GetAll(null, false);
        //     return data.Select(t => t.Key).Distinct().ToArray();
        // }
    }
}