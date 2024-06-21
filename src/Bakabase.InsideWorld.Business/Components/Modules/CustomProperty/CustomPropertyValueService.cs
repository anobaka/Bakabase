using System;
using System.Collections.Generic;
using Bakabase.Abstractions.Services;
using Bakabase.Modules.CustomProperty.Abstractions.Components;
using Bakabase.Modules.CustomProperty.Services;
using Bakabase.Modules.StandardValue.Abstractions.Components;

namespace Bakabase.InsideWorld.Business.Components.Modules.CustomProperty;

public class CustomPropertyValueService(
    IServiceProvider serviceProvider,
    IEnumerable<IStandardValueHandler> converters,
    IEnumerable<ICustomPropertyDescriptor> propertyDescriptors,
    ICustomPropertyLocalizer localizer,
    IBuiltinPropertyValueService builtinPropertyValueService,
    IStandardValueLocalizer standardValueLocalizer) : AbstractCustomPropertyValueService<InsideWorldDbContext>(
    serviceProvider, converters, propertyDescriptors, localizer, builtinPropertyValueService, standardValueLocalizer)
{

}