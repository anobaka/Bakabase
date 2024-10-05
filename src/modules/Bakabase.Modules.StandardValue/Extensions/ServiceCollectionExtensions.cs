using System.Reflection;
using Bakabase.Modules.StandardValue.Abstractions.Components;
using Bakabase.Modules.StandardValue.Abstractions.Configurations;
using Bakabase.Modules.StandardValue.Abstractions.Services;
using Bakabase.Modules.StandardValue.Components;
using Bakabase.Modules.StandardValue.Services;
using Bootstrap.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Modules.StandardValue.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddStandardValue<TScopedDateTimeParser>(this IServiceCollection services)
            where TScopedDateTimeParser : class, ICustomDateTimeParser
        {
            services.AddScoped<IStandardValueService, StandardValueService>();
            services.AddScoped<ICustomDateTimeParser>(sp => sp.GetRequiredService<TScopedDateTimeParser>());
            services.AddTransient<IStandardValueLocalizer, StandardValueLocalizer>();

            return services;
        }
    }
}