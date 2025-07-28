using System.Collections.Generic;
using System.Linq;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bakabase.Modules.Enhancer.Models.Domain.Constants;
using Bootstrap.Extensions;
using static Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain.ResourceOptions;
using static Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain.ResourceOptions.SynchronizationOptionsModel;

namespace Bakabase.InsideWorld.Business.Components.Configurations.Extensions;

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

    public static bool IsSet(this SynchronizationCategoryOptions options)
    {
        return options.DeleteResourcesWithUnknownPath.HasValue ||
               options.MediaLibraryOptionsMap?.Any(x => x.Value.IsSet()) == true ||
               options.EnhancerOptionsMap?.Any(x => x.Value.IsSet()) == true;
    }

    public static SynchronizationCategoryOptions? Optimize(this SynchronizationCategoryOptions options)
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

    public static bool IsSet(this SynchronizationOptionsModel options)
    {
        return options.DeleteResourcesWithUnknownPath.HasValue ||
               options.DeleteResourcesWithUnknownMediaLibrary.HasValue ||
               options.CategoryOptionsMap?.Any(x => x.Value.IsSet()) == true ||
               options.EnhancerOptionsMap?.Any(x => x.Value.IsSet()) == true ||
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

        if (options.CategoryOptionsMap != null)
        {
            Dictionary<int, SynchronizationCategoryOptions>? newMap = null;
            foreach (var (id, o) in options.CategoryOptionsMap)
            {
                var oo = o.Optimize();
                if (oo != null)
                {
                    newMap ??= [];
                    newMap[id] = oo;
                }
            }

            options.CategoryOptionsMap = newMap;
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

    public static bool ShouldBeDeletedSinceFileNotFound(this Abstractions.Models.Domain.Resource fileNotFoundResource,
        SynchronizationOptionsModel? options)
    {
        if (options == null)
        {
            return false;
        }

        var co = options.CategoryOptionsMap?.GetValueOrDefault(fileNotFoundResource.CategoryId);
        var mo = (fileNotFoundResource.IsMediaLibraryV2 ? options.MediaLibraryOptionsMap : co?.MediaLibraryOptionsMap)
            ?.GetValueOrDefault(fileNotFoundResource.MediaLibraryId);
        return mo?.DeleteResourcesWithUnknownPath ??
               co?.DeleteResourcesWithUnknownPath ?? options.DeleteResourcesWithUnknownPath ?? false;
    }

    public static bool ShouldBeDeletedSinceUnknownMediaLibrary(this Abstractions.Models.Domain.Resource fileNotFoundResource,
        SynchronizationOptionsModel? options)
    {
        return options?.DeleteResourcesWithUnknownMediaLibrary ?? false;
    }

    public static int[]? GetIdsOfEnhancersShouldBeReApplied(this Abstractions.Models.Domain.Resource resource,
        SynchronizationOptionsModel? options)
    {
        if (options == null)
        {
            return null;
        }

        var co = options.CategoryOptionsMap?.GetValueOrDefault(resource.CategoryId);
        var mo = (resource.IsMediaLibraryV2 ? options.MediaLibraryOptionsMap : co?.MediaLibraryOptionsMap)
            ?.GetValueOrDefault(resource.MediaLibraryId);

        Dictionary<int, SynchronizationEnhancerOptions>?[] priority =
            [mo?.EnhancerOptionsMap, co?.EnhancerOptionsMap, options.EnhancerOptionsMap];

        return SpecificEnumUtils<EnhancerId>.Values.Cast<int>().Where(id =>
            priority.Select(a => a?.GetValueOrDefault(id)?.ReApply).OfType<bool>().FirstOrDefault()).ToArray();
    }

    public static int[]? GetIdsOfEnhancersShouldBeReEnhanced(this Abstractions.Models.Domain.Resource resource,
        SynchronizationOptionsModel? options)
    {
        if (options == null)
        {
            return null;
        }

        var co = options.CategoryOptionsMap?.GetValueOrDefault(resource.CategoryId);
        var mo = (resource.IsMediaLibraryV2 ? options.MediaLibraryOptionsMap : co?.MediaLibraryOptionsMap)
            ?.GetValueOrDefault(resource.MediaLibraryId);

        Dictionary<int, SynchronizationEnhancerOptions>?[] priority =
            [mo?.EnhancerOptionsMap, co?.EnhancerOptionsMap, options.EnhancerOptionsMap];

        return SpecificEnumUtils<EnhancerId>.Values.Cast<int>().Where(id =>
            priority.Select(a => a?.GetValueOrDefault(id)?.ReEnhance).OfType<bool>().FirstOrDefault()).ToArray();
    }
}