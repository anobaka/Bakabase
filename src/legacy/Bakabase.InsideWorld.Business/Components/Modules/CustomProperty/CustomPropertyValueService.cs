using System;
using System.Collections.Generic;
using Bakabase.Abstractions.Services;
using Bakabase.Modules.CustomProperty.Abstractions.Components;
using Bakabase.Modules.CustomProperty.Services;
using Bakabase.Modules.StandardValue.Abstractions.Components;
using Bakabase.Modules.StandardValue.Abstractions.Services;
using Bakabase.Modules.StandardValue.Services;

namespace Bakabase.InsideWorld.Business.Components.Modules.CustomProperty;

public class CustomPropertyValueService(
    IServiceProvider serviceProvider,
    IEnumerable<IStandardValueHandler> converters, 
    ICustomPropertyDescriptors propertyDescriptors, 
    ICustomPropertyLocalizer localizer,
    IReservedPropertyValueService reservedPropertyValueService, 
    IStandardValueLocalizer standardValueLocalizer,
    IStandardValueService standardValueService) : AbstractCustomPropertyValueService<InsideWorldDbContext>(serviceProvider, converters, propertyDescriptors, localizer, reservedPropertyValueService, standardValueLocalizer, standardValueService);