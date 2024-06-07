﻿using System;
using System.Collections.Generic;
using Bakabase.Modules.CustomProperty.Abstractions.Components;
using Bakabase.Modules.CustomProperty.Services;
using Bakabase.Modules.StandardValue.Abstractions.Components;

namespace Bakabase.InsideWorld.Business.Components.Modules.CustomProperty;

public class CustomPropertyValueService(
    IServiceProvider serviceProvider,
    IEnumerable<IStandardValueHandler> converters,
    IEnumerable<ICustomPropertyDescriptor> propertyDescriptors,
    ICustomPropertyLocalizer localizer,
    IStandardValueLocalizer standardValueLocalizer)
    : AbstractCustomPropertyValueService<InsideWorldDbContext>(serviceProvider, converters, propertyDescriptors,
        localizer, standardValueLocalizer)
{

}