using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Components;
using Bakabase.Modules.Acquisition.Abstractions.Services;
using Bakabase.Modules.Acquisition.Components;
using Bakabase.Modules.Acquisition.Components.Workflow;
using Bakabase.Modules.Acquisition.Services;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bootstrap.Components.Orm;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Modules.Acquisition.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAcquisition<TDbContext>(this IServiceCollection services)
        where TDbContext : DbContext
    {
        services.AddScoped<FullMemoryCacheResourceService<TDbContext, AcquisitionLeadDbModel, int>>();
        services.AddScoped<IAcquisitionLeadService, AcquisitionLeadService<TDbContext>>();

        // Steps are stateless and enumerated from the container, the way enhancers are. The
        // registry is a singleton so a duplicated kind fails once, at startup.
        services.AddSingleton<IAcquisitionStepRegistry, AcquisitionStepRegistry>();

        services.AddScoped<AcquisitionService<TDbContext>>();
        services.AddScoped<IAcquisitionService>(sp => sp.GetRequiredService<AcquisitionService<TDbContext>>());
        services.AddScoped<IAcquisitionQueue>(sp => sp.GetRequiredService<AcquisitionService<TDbContext>>());
        services.AddScoped<AcquisitionRecipeSeeder<TDbContext>>();

        // A recipe is an ordinary workflow: its entry point is a trigger, its item has a type, and
        // its steps are activities. Registering them here is what makes the editor able to show one.
        services.AddSingleton<IWorkflowTrigger, AcquisitionRequestedTrigger>();
        services.AddSingleton<IWorkflowTrigger, AcquisitionStatusChangedTrigger>();
        services.AddSingleton<IWorkflowItemTypeDescriptor, AcquisitionItemTypeDescriptor>();
        services.AddSingleton<IWorkflowItemTypeDescriptor, AcquisitionStatusChangeItemTypeDescriptor>();
        // The outer half: a workflow can also ask for an acquisition.
        services.AddSingleton<IWorkflowActivity, AcquisitionCreateActivity>();

        return services;
    }

    /// <summary>
    /// Registers one acquisition step and, in the same breath, the workflow activity that hosts it.
    /// The pairing is the point: a step that exists but has no activity is a step no recipe can
    /// name, and finding that out at seed time rather than here would be far less obvious.
    /// </summary>
    public static IServiceCollection AddAcquisitionStep<TStep>(this IServiceCollection services)
        where TStep : class, IAcquisitionStep
    {
        services.AddSingleton<TStep>();
        services.AddSingleton<IAcquisitionStep>(sp => sp.GetRequiredService<TStep>());
        services.AddSingleton<IWorkflowActivity>(sp =>
            new AcquisitionStepActivity(sp.GetRequiredService<TStep>()));

        return services;
    }
}
