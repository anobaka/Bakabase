﻿using System;
using Bakabase.Modules.Property.Services;

namespace Bakabase.InsideWorld.Business.Components.Modules.CustomProperty;

public class CategoryCustomPropertyMappingService(IServiceProvider serviceProvider) : AbstractCategoryCustomPropertyMappingService<InsideWorldDbContext>(serviceProvider)
{
    
}