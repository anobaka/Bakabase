using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bootstrap.Components.Orm.Infrastructures;

namespace Bakabase.InsideWorld.Business.Components.Enhancement.Abstractions
{
    public class EnhancementRecordService : ResourceService<InsideWorldDbContext, EnhancementRecord, int>
    {
        public EnhancementRecordService(IServiceProvider serviceProvider) : base(serviceProvider)
        {
        }

        public async Task<List<EnhancementRecord>> GetByResourceId(int resourceId)
        {
            return await base.GetAll(x => x.ResourceId == resourceId);
        }
    }
}
