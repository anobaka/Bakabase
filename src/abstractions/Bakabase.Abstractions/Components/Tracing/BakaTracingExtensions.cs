using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Abstractions.Components.Tracing;

public static class BakaTracingExtensions
{
    public static IServiceCollection AddBakaTracing(this IServiceCollection services)
    {
        return services.AddScoped<BakaTracingContext>(sp =>
        {
            var ctx = new BakaTracingContext();
            ScopedBakaTracingContextAccessor.Current.Value = ctx;
            return ctx;
        });
    }
}