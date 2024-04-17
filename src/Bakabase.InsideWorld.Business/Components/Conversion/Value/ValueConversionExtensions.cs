using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Conversion.Value.Abstractions;
using Bakabase.InsideWorld.Business.Components.Conversion.Value.Converters.Abstractions;
using Bootstrap.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bakabase.InsideWorld.Business.Components.Conversion.Value
{
    public static class ValueConversionExtensions
    {
        public static IServiceCollection AddValueConversion(this IServiceCollection services)
        {
            var types = Assembly.GetAssembly(SpecificTypeUtils<StringValueConverter>.Type)!.GetTypes()
                .Where(s => s is {IsClass: true, IsAbstract: false, IsPublic: true} &&
                            s.IsAssignableTo(SpecificTypeUtils<IValueConverter>.Type)).ToArray();
            foreach (var t in types)
            {
                services.AddScoped(SpecificTypeUtils<IValueConverter>.Type, t);
            }

            return services;
        }
    }
}