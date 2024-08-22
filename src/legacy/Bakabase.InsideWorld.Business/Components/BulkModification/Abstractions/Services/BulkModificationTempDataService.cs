using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models;
using Bootstrap.Components.Orm.Infrastructures;

namespace Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Services
{
    public class BulkModificationTempDataService : ResourceService<InsideWorldDbContext, BulkModificationTempData, int>
    {
        public BulkModificationTempDataService(IServiceProvider serviceProvider) : base(serviceProvider)
        {
        }

        public async Task UpdateResourceIds(int bmId, IEnumerable<int> resourceIds)
        {
            var td = await GetByKey(bmId);
            if (td == null)
            {
                td = new BulkModificationTempData {BulkModificationId = bmId};
                DbContext.BulkModificationTempData.Add(td);
            }

            td.SetResourceIds(resourceIds);
            await DbContext.SaveChangesAsync();
        }
    }
}
