using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Services;
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

        return services;
    }
}
