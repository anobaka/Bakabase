using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.StandardValue.Abstractions.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Modules.StandardValue.Abstractions.Extensions
{
    public static class StandardValueExtensions
    {
        public static IServiceCollection AddStandardValue<TStandardValueService>(this IServiceCollection services) where TStandardValueService: class, IStandardValueService
        {
            return services.AddScoped<IStandardValueService, TStandardValueService>();
        }
    }
}