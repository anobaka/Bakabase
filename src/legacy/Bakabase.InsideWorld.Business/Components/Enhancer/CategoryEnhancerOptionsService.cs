using System;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Modules.Enhancer.Abstractions.Components;
using Bakabase.Modules.Enhancer.Components;
using Bakabase.Modules.Enhancer.Services;
using Bootstrap.Components.Orm.Infrastructures;

namespace Bakabase.InsideWorld.Business.Components.Enhancer;

public class CategoryEnhancerOptionsService(ResourceService<InsideWorldDbContext, CategoryEnhancerOptions, int> orm, IEnhancerDescriptors enhancerDescriptors)
    : AbstractCategoryEnhancerOptionsService<InsideWorldDbContext>(orm, enhancerDescriptors)
{
}