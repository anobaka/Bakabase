using System;
using Bakabase.Modules.CustomProperty.Abstractions.Components;
using Bakabase.Modules.CustomProperty.Services;

namespace Bakabase.InsideWorld.Business.Components.Modules.CustomProperty;

public class CustomPropertyService(IServiceProvider serviceProvider, ICustomPropertyDescriptors propertyDescriptors)
    : AbstractCustomPropertyService<InsideWorldDbContext>(serviceProvider, propertyDescriptors)
{

}