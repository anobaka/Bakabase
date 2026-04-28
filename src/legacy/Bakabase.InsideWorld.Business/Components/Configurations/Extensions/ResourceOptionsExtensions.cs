using static Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain.ResourceOptions;

namespace Bakabase.InsideWorld.Business.Components.Configurations.Extensions;

public static class ResourceOptionsExtensions
{
    public static bool IsSet(this SynchronizationOptionsModel options) =>
        options.SyncMarksImmediately.HasValue;

    public static SynchronizationOptionsModel? Optimize(this SynchronizationOptionsModel options) =>
        options.IsSet() ? options : null;
}
