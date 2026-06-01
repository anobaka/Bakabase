using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Services;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Modules.Workflow.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Modules.Workflow.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddWorkflow<TDbContext>(this IServiceCollection services)
        where TDbContext : DbContext
    {
        services.AddSingleton<IWorkflowTriggerRegistry, WorkflowTriggerRegistry>();
        services.AddSingleton<IWorkflowActivityRegistry, WorkflowActivityRegistry>();
        services.AddSingleton<IWorkflowItemTypeRegistry, WorkflowItemTypeRegistry>();
        services.AddScoped<WorkflowRunner<TDbContext>>();
        services.AddScoped<IWorkflowEventBus, WorkflowEventBus<TDbContext>>();
        services.AddScoped<IWorkflowDefinitionService, WorkflowDefinitionService<TDbContext>>();
        services.AddScoped<WorkflowRunRehydrator<TDbContext>>();
        return services;
    }
}
