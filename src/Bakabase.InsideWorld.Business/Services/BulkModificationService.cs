using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models;
using Bootstrap.Components.Orm.Infrastructures;

namespace Bakabase.InsideWorld.Business.Services
{
    public class BulkModificationService : ResourceService<InsideWorldDbContext, BulkModification, int>
    {
        public BulkModificationService(IServiceProvider serviceProvider) : base(serviceProvider)
        {
        }

        public async Task<BulkModificationDto> GetDto(int id)
        {
            var d = await GetByKey(id);
            return d.ToDto();
        }
    }
}
