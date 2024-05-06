﻿using System.Linq.Expressions;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Modules.Enhancer.Models.Domain;
using Bakabase.Modules.Enhancer.Models.Input;
using Bootstrap.Models.ResponseModels;

namespace Bakabase.Modules.Enhancer.Abstractions.Services
{
    public interface ICategoryEnhancerOptionsService
    {
        Task<List<CategoryEnhancerFullOptions>> GetAll(Expression<Func<CategoryEnhancerOptions, bool>>? exp);
        Task<BaseResponse> Patch(int categoryId, int enhancerId, CategoryEnhancerOptionsPatchInputModel model);
    }
}