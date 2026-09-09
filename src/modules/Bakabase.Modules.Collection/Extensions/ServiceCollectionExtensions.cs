using Bakabase.Modules.Collection.Abstractions.Models.Db;
using Bakabase.Modules.Collection.Abstractions.Services;
using Bakabase.Modules.Collection.Components.Workflow;
using Bakabase.Modules.Collection.Services;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bootstrap.Components.Orm;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Modules.Collection.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCollections<TDbContext>(this IServiceCollection services)
        where TDbContext : DbContext
    {
        services.AddScoped<FullMemoryCacheResourceService<TDbContext, CollectionDbModel, int>>();
        services.AddScoped<FullMemoryCacheResourceService<TDbContext, CollectionResourceMappingDbModel, int>>();
        services.AddScoped<ICollectionResourceMappingService, CollectionResourceMappingService<TDbContext>>();

        // Both directions between collections and workflows: something joining a collection is an
        // event to build on, and filing a resource under a name is something a chain can do.
        services.AddSingleton<IWorkflowTrigger, CollectionMembersAddedTrigger>();
        services.AddSingleton<IWorkflowItemTypeDescriptor, CollectionMemberItemTypeDescriptor>();
        services.AddSingleton<IWorkflowActivity, CollectionAddResourceActivity>();

        return services;
    }
}
