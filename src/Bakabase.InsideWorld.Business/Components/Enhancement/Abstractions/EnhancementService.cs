using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Enhancement.Abstractions.Models.Db;
using Bakabase.InsideWorld.Business.Components.StandardValue;
using Bakabase.InsideWorld.Business.Components.StandardValue.Abstractions;
using Bootstrap.Components.Orm.Infrastructures;

namespace Bakabase.InsideWorld.Business.Components.Enhancement.Abstractions
{
    public class EnhancementService : ResourceService<InsideWorldDbContext, Models.Db.Enhancement, int>
    {
        public EnhancementService(IServiceProvider serviceProvider) : base(serviceProvider)
        {
        }

        public async Task<List<Models.Domain.Enhancement>> GetAll(Expression<Func<Models.Db.Enhancement, bool>>? exp)
        {
            var data = await base.GetAll(exp);
            return data.Select(d => d.ToDomainModel()!).ToList();
        }

        public async Task AddRange(List<Models.Domain.Enhancement> enhancements)
        {
            var dbValues = enhancements.Select(e => e.ToDbModel()!);
            await AddRange(dbValues);
        }

        public async Task UpdateRange(List<Models.Domain.Enhancement> enhancements)
        {
            var dbValues = enhancements.Select(e => e.ToDbModel()!);
            await UpdateRange(dbValues);
        }
    }
}