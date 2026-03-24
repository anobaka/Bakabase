using System.Reflection;
using Bakabase.Modules.ResourceResolver.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bakabase.Modules.ResourceResolver.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers all IResourceResolver implementations found in the specified assemblies.
    /// If no assemblies are specified, scans all loaded assemblies.
    /// </summary>
    public static IServiceCollection AddResourceResolvers(this IServiceCollection services,
        params Assembly[] assemblies)
    {
        var assembliesToScan = assemblies.Length > 0
            ? assemblies
            : AppDomain.CurrentDomain.GetAssemblies();

        var resolverTypes = assembliesToScan
            .SelectMany(a =>
            {
                try { return a.GetTypes(); }
                catch { return Array.Empty<Type>(); }
            })
            .Where(t => t is { IsClass: true, IsAbstract: false, IsPublic: true }
                        && typeof(IResourceResolver).IsAssignableFrom(t))
            .Distinct()
            .ToList();

        foreach (var resolverType in resolverTypes)
        {
            services.AddScoped(typeof(IResourceResolver), resolverType);
        }

        return services;
    }
}
