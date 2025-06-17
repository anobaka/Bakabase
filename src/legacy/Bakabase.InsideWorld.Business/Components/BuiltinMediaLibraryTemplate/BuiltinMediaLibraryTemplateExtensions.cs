using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.InsideWorld.Business.Components.BuiltinMediaLibraryTemplate;

public static class BuiltinMediaLibraryTemplateExtensions
{
    public static IServiceCollection AddBuiltinMediaLibraryTemplates(this IServiceCollection services)
    {
        return services
            .AddTransient<IBuiltinMediaLibraryTemplateLocalizer, InsideWorldLocalizer>(x => x.GetRequiredService<InsideWorldLocalizer>())
            .AddScoped<BuiltinMediaLibraryTemplateService>();
    }
}