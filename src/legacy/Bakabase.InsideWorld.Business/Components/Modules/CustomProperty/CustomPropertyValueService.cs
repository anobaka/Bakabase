using System;
using Bakabase.Modules.Property.Abstractions.Components;
using Bakabase.Modules.Property.Services;
using Bakabase.Modules.StandardValue.Abstractions.Components;
using Bakabase.Modules.StandardValue.Abstractions.Services;

namespace Bakabase.InsideWorld.Business.Components.Modules.CustomProperty;

public class CustomPropertyValueService(
    IServiceProvider serviceProvider,
    IPropertyLocalizer localizer,
    IStandardValueLocalizer standardValueLocalizer,
    IStandardValueService standardValueService) : AbstractCustomPropertyValueService<InsideWorldDbContext>(serviceProvider, localizer, standardValueLocalizer, standardValueService);