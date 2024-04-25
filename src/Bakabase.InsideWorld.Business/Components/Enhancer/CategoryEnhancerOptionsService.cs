using System;
using Bakabase.Modules.Enhancer.Services;

namespace Bakabase.InsideWorld.Business.Components.Enhancer;

public class CategoryEnhancerOptionsService(IServiceProvider serviceProvider) : AbstractCategoryEnhancerOptionsService<InsideWorldDbContext>(serviceProvider)
{
    
}