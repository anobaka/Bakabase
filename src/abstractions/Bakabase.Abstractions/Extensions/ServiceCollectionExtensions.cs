using Bakabase.Abstractions.Components.FileSystem;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bakabase.Abstractions.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBakabaseComponents(this IServiceCollection services)
    {
        services.TryAddSingleton<IFileManager, FileManager>();
        return services;
    }
}