using System.Collections.Generic;
using System.Linq;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Modules.Enhancer.Models.Domain.Constants;
using Bootstrap.Extensions;
using static Bakabase.InsideWorld.Business.Configurations.Models.Domain.ResourceOptions;
using static Bakabase.InsideWorld.Business.Configurations.Models.Domain.ResourceOptions.SynchronizationOptionsModel;

namespace Bakabase.InsideWorld.Business.Configurations.Extensions;

public static class ResourceOptionsExtensions
{
    public static bool IsSet(this SynchronizationEnhancerOptions options) =>
        options.ReApply.HasValue || options.ReEnhance.HasValue;

    public static SynchronizationEnhancerOptions? Optimize(this SynchronizationEnhancerOptions options) =>
        !options.IsSet() ? null : options;

    public static bool IsSet(this SynchronizationMediaLibraryOptions options)
    {
        return options.DeleteResourcesWithUnknownPath.HasValue ||
               options.EnhancerOptionsMap?.Any(x => x.Value.IsSet()) == true;
    }

    public static SynchronizationMediaLibraryOptions? Optimize(this SynchronizationMediaLibraryOptions options)
    {
        if (options.EnhancerOptionsMap != null)
        {
            Dictionary<int, SynchronizationEnhancerOptions>? newMap = null;
            foreach (var (id, o) in options.EnhancerOptionsMap)
            {
                var oo = o.Optimize();
                if (oo != null)
                {
                    newMap ??= [];
                    newMap[id] = oo;
                }
            }

            options.EnhancerOptionsMap = newMap;
        }

        return options.IsSet() ? options : null;
    }

    public static bool IsSet(this SynchronizationOptionsModel options)
    {
        return options.DeleteResourcesWithUnknownPath.HasValue ||
               options.DeleteResourcesWithUnknownMediaLibrary.HasValue ||
               options.MediaLibraryOptionsMap?.Any(x => x.Value.IsSet()) == true;
    }

    public static SynchronizationOptionsModel? Optimize(this SynchronizationOptionsModel options)
    {
        if (options.EnhancerOptionsMap != null)
        {
            Dictionary<int, SynchronizationEnhancerOptions>? newMap = null;
            foreach (var (id, o) in options.EnhancerOptionsMap)
            {
                var oo = o.Optimize();
                if (oo != null)
                {
                    newMap ??= [];
                    newMap[id] = oo;
                }
            }

            options.EnhancerOptionsMap = newMap;
        }

        if (options.MediaLibraryOptionsMap != null)
        {
            Dictionary<int, SynchronizationMediaLibraryOptions>? newMap = null;
            foreach (var (id, o) in options.MediaLibraryOptionsMap)
            {
                var oo = o.Optimize();
                if (oo != null)
                {
                    newMap ??= [];
                    newMap[id] = oo;
                }
            }

            options.MediaLibraryOptionsMap = newMap;
        }

        return options.IsSet() ? options : null;
    }

    public static bool ShouldBeDeletedSinceFileNotFound(this Resource fileNotFoundResource,
        SynchronizationOptionsModel? options)
    {
        if (options == null)
        {
            return false;
        }

        var mo = options.MediaLibraryOptionsMap?.GetValueOrDefault(fileNotFoundResource.MediaLibraryId);
        return mo?.DeleteResourcesWithUnknownPath ?? options.DeleteResourcesWithUnknownPath ?? false;
    }

    public static bool ShouldBeDeletedSinceUnknown(this Resource fileNotFoundResource,
        SynchronizationOptionsModel? options)
    {
        return options?.DeleteResourcesWithUnknownMediaLibrary ?? false;
    }

    public static int[]? GetIdsOfEnhancersShouldBeReApplied(this Resource resource,
        SynchronizationOptionsModel? options)
    {
        if (options == null)
        {
            return null;
        }

        var mo = options.MediaLibraryOptionsMap?.GetValueOrDefault(resource.MediaLibraryId);

        Dictionary<int, SynchronizationEnhancerOptions>?[] priority =
            [mo?.EnhancerOptionsMap, options.EnhancerOptionsMap];

        return SpecificEnumUtils<EnhancerId>.Values.Cast<int>().Where(id =>
            priority.Select(a => a?.GetValueOrDefault(id)?.ReApply).OfType<bool>().FirstOrDefault()).ToArray();
    }

    public static int[]? GetIdsOfEnhancersShouldBeReEnhanced(this Resource resource,
        SynchronizationOptionsModel? options)
    {
        if (options == null)
        {
            return null;
        }

        var mo = options.MediaLibraryOptionsMap?.GetValueOrDefault(resource.MediaLibraryId);

        Dictionary<int, SynchronizationEnhancerOptions>?[] priority =
            [mo?.EnhancerOptionsMap, options.EnhancerOptionsMap];

        return SpecificEnumUtils<EnhancerId>.Values.Cast<int>().Where(id =>
            priority.Select(a => a?.GetValueOrDefault(id)?.ReEnhance).OfType<bool>().FirstOrDefault()).ToArray();
    }
}