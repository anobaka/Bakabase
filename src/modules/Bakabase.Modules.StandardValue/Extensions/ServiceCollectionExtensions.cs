using System.Reflection;
using Bakabase.Modules.StandardValue.Abstractions.Components;
using Bakabase.Modules.StandardValue.Abstractions.Services;
using Bakabase.Modules.StandardValue.Components;
using Bakabase.Modules.StandardValue.Services;
using Bootstrap.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Modules.StandardValue.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection
            AddStandardValue<TDateTimeParser, TStandardValueLocalizer>(this IServiceCollection services)
            where TDateTimeParser : class, IDateTimeParser where TStandardValueLocalizer : IStandardValueLocalizer
        {
            var types = Assembly.GetExecutingAssembly()!.GetTypes()
                .Where(s => s is {IsClass: true, IsAbstract: false, IsPublic: true} &&
                            s.IsAssignableTo(SpecificTypeUtils<IStandardValueHandler>.Type)).ToArray();
            foreach (var t in types)
            {
                services.AddScoped(SpecificTypeUtils<IStandardValueHandler>.Type, t);
            }

            services.AddScoped<IStandardValueHandlers, StandardValueHandlers>();
            services.AddScoped<IStandardValueService, StandardValueService>();
            services.AddScoped<IDateTimeParser>(sp => sp.GetRequiredService<TDateTimeParser>());
            services.AddTransient<IStandardValueLocalizer>(sp => sp.GetRequiredService<TStandardValueLocalizer>());
            services.AddSingleton<IStandardValueHelper, StandardValueHelper>();

            return services;
        }
    }
}