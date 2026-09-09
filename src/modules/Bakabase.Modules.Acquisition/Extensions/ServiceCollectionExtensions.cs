using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Components;
using Bakabase.Modules.Acquisition.Abstractions.Services;
using Bakabase.Modules.Acquisition.Components;
using Bakabase.Modules.Acquisition.Services;
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

        return services;
    }
}
