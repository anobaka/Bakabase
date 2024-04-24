using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;
using Bakabase.Abstractions.Extensions;
using Bakabase.InsideWorld.Business.Components.StandardValue;
using Bootstrap.Components.Orm.Infrastructures;

namespace Bakabase.InsideWorld.Business.Components.Enhancement.Abstractions
{
    public class EnhancementService : ResourceService<InsideWorldDbContext, Bakabase.Abstractions.Models.Db.Enhancement, int>
    {
        public EnhancementService(IServiceProvider serviceProvider) : base(serviceProvider)
        {
        }

        public async Task<List<Bakabase.Abstractions.Models.Domain.Enhancement>> GetAll(Expression<Func<Bakabase.Abstractions.Models.Db.Enhancement, bool>>? exp)
        {
            var data = await base.GetAll(exp);
            return data.Select(d => d.ToDomainModel()!).ToList();
        }

        public async Task AddRange(List<Bakabase.Abstractions.Models.Domain.Enhancement> enhancements)
        {
            var dbValues = enhancements.Select(e => e.ToDbModel()!);
            await AddRange(dbValues);
        }

        public async Task UpdateRange(List<Bakabase.Abstractions.Models.Domain.Enhancement> enhancements)
        {
            var dbValues = enhancements.Select(e => e.ToDbModel()!);
            await UpdateRange(dbValues);
        }
    }
}