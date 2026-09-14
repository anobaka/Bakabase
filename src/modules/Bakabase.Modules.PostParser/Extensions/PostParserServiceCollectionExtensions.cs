using Bakabase.Modules.PostParser.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bakabase.Modules.PostParser.Extensions;

public static class PostParserServiceCollectionExtensions
{
    /// <summary>Registers extraction; the host supplies an IPostContentService backed by its platform readers.</summary>
    public static IServiceCollection AddPostParserCapabilities(this IServiceCollection services)
    {
        services.TryAddScoped<IPostDownloadInfoExtractor, PostDownloadInfoExtractor>();
        return services;
    }
}
