using Bakabase.Abstractions.Components.Text;
using Bakabase.Abstractions.Services;
using Bakabase.Modules.Text.Components;
using Bakabase.Modules.Text.Services;
using Bootstrap.Components.Orm;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Modules.Text.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddText<TDbContext>(this IServiceCollection services)
        where TDbContext : DbContext
    {
        services.AddScoped<FullMemoryCacheResourceService<TDbContext, Abstractions.Models.Db.TextType, int>>();
        services.AddScoped<FullMemoryCacheResourceService<TDbContext, Abstractions.Models.Db.TextEntry, int>>();
        services.AddScoped<ITextVocabularyService, TextVocabularyService<TDbContext>>();

        // TextOps is also the app's ICustomDateTimeParser; AddStandardValue<TextOps> binds that.
        services.AddScoped<TextOps>();
        services.AddScoped<ITextOps>(sp => sp.GetRequiredService<TextOps>());

        return services;
    }
}
