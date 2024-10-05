using System;
using Bakabase.Modules.Property.Services;

namespace Bakabase.InsideWorld.Business.Components.Modules.CustomProperty;

public class CustomPropertyService(IServiceProvider serviceProvider) : AbstractCustomPropertyService<InsideWorldDbContext>(serviceProvider)
{

}