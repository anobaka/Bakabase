using Bakabase.Modules.Subscription.Abstractions.Components;
using Bakabase.Modules.Subscription.Abstractions.Services;
using Bakabase.Modules.Subscription.Components;
using Bakabase.Modules.Subscription.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Modules.Subscription.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSubscription<TDbContext>(this IServiceCollection services)
        where TDbContext : DbContext
    {
        services.AddSingleton<ISubscriptionProviderRegistry, SubscriptionProviderRegistry>();
        services.AddScoped<ISubscriptionService, SubscriptionService<TDbContext>>();
        return services;
    }
}
