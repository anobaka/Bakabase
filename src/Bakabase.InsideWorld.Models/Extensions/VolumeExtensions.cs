using System;
using System.Collections.Generic;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Aos;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bakabase.InsideWorld.Models.Models.Entities;

namespace Bakabase.InsideWorld.Models.Extensions
{
    public static class VolumeExtensions
    {
        public static VolumeDto? ToDto(this Volume? v)
        {
            if (v == null)
            {
                return null;
            }

            return new()
            {
                Name = v.Name,
                Title = v.Title,
                Index = v.Index,
                SerialId = v.SerialId,
                ResourceId = v.ResourceId
            };
        }

        public static Volume? ToResource(this VolumeDto? v)
        {
            if (v == null)
            {
                return null;
            }

            return new()
            {
                Index = v.Index,
                Name = v.Name,
                Title = v.Title,
                ResourceId = v.ResourceId,
                SerialId = v.SerialId
            };
        }

        public static List<ResourceDiff>? Compare(this VolumeDto? a, VolumeDto? b)
        {
            var indexDiff = ResourceDiff.BuildRootDiff(ResourceDiffProperty.Volume, a?.Index, b?.Index,
                EqualityComparer<int?>.Default, nameof(VolumeDto.Index), null);
            var nameDiff = ResourceDiff.BuildRootDiff(ResourceDiffProperty.Volume, a?.Name, b?.Name,
                StringComparer.OrdinalIgnoreCase, nameof(VolumeDto.Name), null);
            var titleDiff = ResourceDiff.BuildRootDiff(ResourceDiffProperty.Volume, a?.Title, b?.Title,
                StringComparer.OrdinalIgnoreCase, nameof(VolumeDto.Title), null);

            if (indexDiff != null || nameDiff != null || titleDiff != null)
            {
                var diffs = new List<ResourceDiff>();
                if (indexDiff != null)
                {
                    diffs.Add(indexDiff);
                }

                if (nameDiff != null)
                {
                    diffs.Add(nameDiff);
                }

                return diffs;
            }

            return null;
        }

        public static VolumeDto Clone(this VolumeDto volume)
        {
            return volume with { };
        }
    }
}