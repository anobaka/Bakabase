using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models;
using Bootstrap.Components.Orm.Infrastructures;

namespace Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Services
{
    public class BulkModificationDiffService : ResourceService<InsideWorldDbContext, BulkModificationDiff, int>
    {
        protected BulkModificationService BulkModificationService => GetRequiredService<BulkModificationService>();

        public BulkModificationDiffService(IServiceProvider serviceProvider) : base(serviceProvider)
        {
        }

        public async Task UpdateAll(int bmId, List<BulkModificationDiff> diffs)
        {
            await RemoveAll(x => x.BulkModificationId == bmId);
            await AddRange(diffs);
        }

        public async Task<List<BulkModificationDiff>> GetByBmId(int bmId)
        {
            var diffs = (await GetAll(x => x.BulkModificationId == bmId))!;
            return diffs;
        }
    }
}