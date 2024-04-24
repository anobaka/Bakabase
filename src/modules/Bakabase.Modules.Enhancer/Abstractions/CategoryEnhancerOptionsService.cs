using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Db;
using Bootstrap.Components.Orm.Infrastructures;

namespace Bakabase.InsideWorld.Business.Components.Enhancement.Abstractions
{
    public class CategoryEnhancerOptionsService : ResourceService<InsideWorldDbContext, CategoryEnhancerOptions, int>
    {
        public CategoryEnhancerOptionsService(IServiceProvider serviceProvider) : base(serviceProvider)
        {
        }

        public async Task<List<Bakabase.Abstractions.Models.Domain.CategoryEnhancerOptions>> GetAll(Expression<Func<CategoryEnhancerOptions, bool>>? exp)
        {
            var data = await base.GetAll(exp, false);
            return data.Select(d => d.ToDomainModel()!).ToList();
        }
    }
}