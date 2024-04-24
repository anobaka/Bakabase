using System.Linq;
using System.Reflection;
using Bakabase.InsideWorld.Business.Components.StandardValue.Abstractions;
using Bootstrap.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.InsideWorld.Business.Components.StandardValue
{
    public static class StandardValueConversionExtensions
    {
        public static IServiceCollection AddValueConversion(this IServiceCollection services)
        {
            var types = Assembly.GetExecutingAssembly()!.GetTypes()
                .Where(s => s is { IsClass: true, IsAbstract: false, IsPublic: true } &&
                            s.IsAssignableTo(SpecificTypeUtils<IStandardValueHandler>.Type)).ToArray();
            foreach (var t in types)
            {
                services.AddScoped(SpecificTypeUtils<IStandardValueHandler>.Type, t);
            }

            return services;
        }
    }
}