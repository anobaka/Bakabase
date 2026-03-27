using System;
using System.Linq;
using System.Reflection;
using Bakabase.Abstractions.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.InsideWorld.Business.Components.Resolvers;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers all IResourceResolver implementations found in the specified assemblies.
    /// If no assemblies are specified, scans all loaded assemblies.
    /// </summary>
    public static IServiceCollection AddResourceResolvers(this IServiceCollection services,
        params Assembly[] assemblies)
    {
        var assembliesToScan = GetAssemblies(assemblies);
        RegisterAll<IResourceResolver>(services, assembliesToScan);
        return services;
    }

    /// <summary>
    /// Registers all ICoverProvider implementations found in the specified assemblies.
    /// </summary>
    public static IServiceCollection AddCoverProviders(this IServiceCollection services,
        params Assembly[] assemblies)
    {
        var assembliesToScan = GetAssemblies(assemblies);
        RegisterAll<ICoverProvider>(services, assembliesToScan);
        return services;
    }

    /// <summary>
    /// Registers all IPlayableItemProvider implementations found in the specified assemblies.
    /// </summary>
    public static IServiceCollection AddPlayableItemProviders(this IServiceCollection services,
        params Assembly[] assemblies)
    {
        var assembliesToScan = GetAssemblies(assemblies);
        RegisterAll<IPlayableItemProvider>(services, assembliesToScan);
        return services;
    }

    /// <summary>
    /// Registers all IMetadataProvider implementations found in the specified assemblies.
    /// </summary>
    public static IServiceCollection AddMetadataProviders(this IServiceCollection services,
        params Assembly[] assemblies)
    {
        var assembliesToScan = GetAssemblies(assemblies);
        RegisterAll<IMetadataProvider>(services, assembliesToScan);
        return services;
    }

    private static Assembly[] GetAssemblies(Assembly[] assemblies) =>
        assemblies.Length > 0 ? assemblies : AppDomain.CurrentDomain.GetAssemblies();

    private static void RegisterAll<TInterface>(IServiceCollection services, Assembly[] assemblies)
    {
        var types = assemblies
            .SelectMany(a =>
            {
                try { return a.GetTypes(); }
                catch { return Array.Empty<Type>(); }
            })
            .Where(t => t is { IsClass: true, IsAbstract: false, IsPublic: true }
                        && typeof(TInterface).IsAssignableFrom(t))
            .Distinct()
            .ToList();

        foreach (var type in types)
        {
            services.AddScoped(typeof(TInterface), type);
        }
    }
}
