using Bakabase.InsideWorld.Models.Models.Dtos;
using Bakabase.InsideWorld.Models.Models.Entities;

namespace Bakabase.InsideWorld.Models.Extensions
{
    public static class VolumeExtensions
    {
        public static VolumeDto ToDto(this Volume v)
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

        public static Volume ToResource(this VolumeDto v)
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
    }
}