using System.Linq.Expressions;
using Bakabase.Abstractions.Models.Db;

namespace Bakabase.Modules.Enhancer.Abstractions.Services
{
    public interface ICategoryEnhancerOptionsService
    {
        Task<List<Bakabase.Abstractions.Models.Domain.CategoryEnhancerOptions>> GetAll(Expression<Func<CategoryEnhancerOptions, bool>>? exp);
    }
}