using System;
using System.Linq;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bootstrap.Extensions;
using Newtonsoft.Json;

namespace Bakabase.InsideWorld.Models.Extensions
{
    public static class MediaLibraryExtensions
    {
        public static MediaLibraryDto.PathConfigurationDto? ToDto(this MediaLibrary.PathConfiguration? pc)
        {
            if (pc == null)
            {
                return null;
            }

            return new MediaLibraryDto.PathConfigurationDto
            {
                FixedTagIds = pc.FixedTagIds,
                Path = pc.Path,
                RpmValues = pc.RpmValues, 
                Regex = pc.Regex
            };
        }

        public static MediaLibraryDto? ToDto(this MediaLibrary? ml)
        {
            if (ml == null)
            {
                return null;
            }

            var dto = new MediaLibraryDto
            {
                CategoryId = ml.CategoryId,
                Name = ml.Name,
                Order = ml.Order,
                Id = ml.Id,
                ResourceCount = ml.ResourceCount
            };

            if (ml.PathConfigurationsJson.IsNotEmpty())
            {
                try
                {
                    dto.PathConfigurations =
                        JsonConvert.DeserializeObject<MediaLibrary.PathConfiguration[]>(ml.PathConfigurationsJson!)!
                            .Select(t => t.ToDto()).ToArray()!;
                }
                catch (Exception e)
                {

                }
            }

            // Paths' data in previous versions may be duplicated
            dto.RootPathInformation = dto.PathConfigurations.GroupBy(t => t.Path).ToDictionary(t => t.Key,
                t => new MediaLibraryDto.SingleMediaLibraryRootPathInformation());
            return dto;
        }
    }
}