using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Enhancement.Abstractions.Models.Domain;
using Bootstrap.Components.Orm.Infrastructures;

namespace Bakabase.InsideWorld.Business.Components.Enhancement.Abstractions
{
    public class CategoryEnhancerOptionsService : ResourceService<InsideWorldDbContext, Models.Db.CategoryEnhancerOptions, int>
    {
        public CategoryEnhancerOptionsService(IServiceProvider serviceProvider) : base(serviceProvider)
        {
        }

        public async Task<List<CategoryEnhancerOptions>> GetAll(Expression<Func<Models.Db.CategoryEnhancerOptions, bool>>? exp)
        {
            var data = await base.GetAll(exp, false);
            return data.Select(d => d.ToDomainModel()!).ToList();
        }
    }
}