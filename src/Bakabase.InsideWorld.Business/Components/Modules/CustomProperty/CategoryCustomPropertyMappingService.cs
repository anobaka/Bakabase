using System;
using Bakabase.Modules.CustomProperty.Services;

namespace Bakabase.InsideWorld.Business.Components.Modules.CustomProperty;

public class CategoryCustomPropertyMappingService(IServiceProvider serviceProvider) : AbstractCategoryCustomPropertyMappingService<InsideWorldDbContext>(serviceProvider)
{
    
}