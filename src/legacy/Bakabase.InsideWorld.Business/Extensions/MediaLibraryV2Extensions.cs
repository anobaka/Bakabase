using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Property.Extensions;

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

    public static Resource ToDomainModel(this TempSyncResource tempSyncResource, int mediaLibraryId)
    {
        return new Resource
        {
            MediaLibraryId = mediaLibraryId,
            Path = tempSyncResource.Path,
            Directory = Path.GetDirectoryName(tempSyncResource.Path)!,
            IsFile = tempSyncResource.IsFile,
            Parent = tempSyncResource.Parent != null ? ToDomainModel(tempSyncResource.Parent, mediaLibraryId) : null,
            FileCreatedAt = tempSyncResource.FileCreatedAt,
            FileModifiedAt = tempSyncResource.FileModifiedAt,
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now,
            Properties = tempSyncResource.PropertyValues?.GroupBy(d => d.Key.Pool).ToDictionary(d => (int) d.Key,
                d => d.ToDictionary(c => c.Key.Id,
                    c => new Resource.Property(c.Key.Name, c.Key.Type, c.Key.Type.GetDbValueType(),
                        c.Key.Type.GetBizValueType(),
                        [
                            new Resource.Property.PropertyValue((int) PropertyValueScope.Synchronization, null, c.Value,
                                null)
                        ])))
        };
    }
}