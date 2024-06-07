using System;
using Bakabase.Modules.CustomProperty.Services;

namespace Bakabase.InsideWorld.Business.Components.Modules.CustomProperty;

public class CustomPropertyService(IServiceProvider serviceProvider) : AbstractCustomPropertyService<InsideWorldDbContext>(serviceProvider)
{
    
}