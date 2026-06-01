using Bakabase.Modules.Notification.Abstractions.Services;
using Bakabase.Modules.Notification.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Modules.Notification.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddNotification<TDbContext>(this IServiceCollection services)
        where TDbContext : DbContext
    {
        services.AddScoped<INotificationService, NotificationService<TDbContext>>();
        return services;
    }
}
