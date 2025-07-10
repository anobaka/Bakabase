using Bakabase.InsideWorld.Business.Components.FileNameModifier.Abstractions;
using Bakabase.InsideWorld.Business.Components.FileNameModifier.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.InsideWorld.Business.Components.FileNameModifier.Extensions;

public static class FileNameModifierExtensions
{
    public static IServiceCollection AddFileNameModifier(this IServiceCollection services)
    {
        services.AddSingleton<IFileNameModifier, Components.FileNameModifier>();
        return services;
    }
}