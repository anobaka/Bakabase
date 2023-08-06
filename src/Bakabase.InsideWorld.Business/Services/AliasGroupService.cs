using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bootstrap.Components.Orm.Infrastructures;

namespace Bakabase.InsideWorld.Business.Services
{
    public class AliasGroupService: ResourceService<InsideWorldDbContext, AliasGroup, int>
    {
        public AliasGroupService(IServiceProvider serviceProvider) : base(serviceProvider)
        {
        }

        public async Task<int[]> RequestNewGroupIds(int count)
        {
            var groups = new List<AliasGroup>();
            for (var i = 0; i < count; i++)
            {
                groups.Add(new AliasGroup());
            }

            var data = await AddRange(groups);

            return data.Data.Select(t => t.Id).ToArray();
        }
    }
}
