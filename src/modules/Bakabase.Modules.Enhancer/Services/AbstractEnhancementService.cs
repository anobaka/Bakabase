using System.Linq.Expressions;
using Bakabase.Abstractions.Extensions;
using Bakabase.Modules.Enhancer.Abstractions.Services;
using Bootstrap.Components.Orm.Infrastructures;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Modules.Enhancer.Services
{
    public class AbstractEnhancementService<TDbContext>(IServiceProvider serviceProvider)
        : ResourceService<TDbContext, Bakabase.Abstractions.Models.Db.Enhancement, int>(serviceProvider),
            IEnhancementService where TDbContext : DbContext
    {
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