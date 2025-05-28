using System.Collections.Generic;
using Bakabase.Abstractions.Models.Domain;

namespace Bakabase.InsideWorld.Business.Extensions;

public static class MediaLibraryV2Extensions
{
    public static List<TempSyncResource> Flatten(this TempSyncResource resource)
    {
        var list = new List<TempSyncResource> {resource};
        if (resource.Children != null)
        {
            foreach (var c in resource.Children)
            {
                list.AddRange(c.Flatten());
            }
        }

        return list;
    }
}