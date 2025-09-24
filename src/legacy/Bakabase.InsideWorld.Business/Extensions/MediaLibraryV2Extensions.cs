using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Property.Extensions;

namespace Bakabase.InsideWorld.Business.Extensions;

public static class MediaLibraryV2Extensions
{
    public static IEnumerable<Resource> Flatten(this TempSyncResource tempSyncResource, int mediaLibraryId,
        Resource? parent)
    {
        var r = new Resource
        {
            Id = tempSyncResource.Id,
            MediaLibraryId = mediaLibraryId,
            Path = tempSyncResource.Path,
            Directory = Path.GetDirectoryName(tempSyncResource.Path)!.StandardizePath()!,
            IsFile = tempSyncResource.IsFile,
            Parent = parent,
            FileCreatedAt = tempSyncResource.FileCreatedAt,
            FileModifiedAt = tempSyncResource.FileModifiedAt,
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now,
            Properties = tempSyncResource.PropertyValues?.GroupBy(d => d.Key.Pool).ToDictionary(d => (int) d.Key,
                d => d.ToDictionary(c => c.Key.Id,
                    c => new Resource.Property(c.Key.Name, c.Key.Type, c.Key.Type.GetDbValueType(),
                        c.Key.Type.GetBizValueType(),
                        [
                            new Resource.Property.PropertyValue((int) PropertyValueScope.Synchronization, null,
                                c.Value,
                                null)
                        ])))
        };

        yield return r;

        if (tempSyncResource.Children == null)
        {
            yield break;
        }

        foreach (var cr in tempSyncResource.Children.SelectMany(c => c.Flatten(mediaLibraryId, r)))
        {
            cr.Parent = r;
            yield return cr;
        }
    }
}