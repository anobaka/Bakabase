using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.Property.Abstractions.Components;
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.Modules.Property.Components;
using Bakabase.Modules.Property.Components.DataSync;
using Bakabase.Modules.Property.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Modules.Property.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddProperty<TDbContext>(this IServiceCollection services)
        where TDbContext : DbContext
    {
        services.AddScoped<IPropertyService, PropertyService>();
        services.AddScoped<ICustomPropertyService, CustomPropertyService<TDbContext>>();
        services.AddScoped<ICustomPropertyValueService, CustomPropertyValueService<TDbContext>>();
        services.AddTransient<IPropertyLocalizer, PropertyLocalizer>();
        services.AddScoped<IPropertyTypeConverter, PropertyTypeConverter>();

        // Data sync's customProperty kind, next to the service that owns the table; resolved only on use.
        services.AddScoped<IDataSyncKind, CustomPropertyDataSyncKind<TDbContext>>();

        services.AddTransient<BuiltinPropertyMap>();

        return services;
    }
}