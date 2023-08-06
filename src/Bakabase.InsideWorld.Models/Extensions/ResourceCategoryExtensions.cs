using System;
using System.Linq;
using Bakabase.InsideWorld.Models.Models.Aos;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bootstrap.Extensions;
using Newtonsoft.Json;

namespace Bakabase.InsideWorld.Models.Extensions
{
    public static class ResourceCategoryExtensions
    {
        public static ResourceCategoryDto ToDto(this ResourceCategory c)
        {
            if (c == null)
            {
                return null;
            }

            var rc = new ResourceCategoryDto
            {
                Color = c.Color,
                CreateDt = c.CreateDt,
                Id = c.Id,
                Name = c.Name,
                CoverSelectionOrder = c.CoverSelectionOrder,
                Order = c.Order,
                GenerateNfo = c.GenerateNfo
            };

            if (c.EnhancementOptionsJson.IsNotEmpty())
            {
                try
                {
                    rc.EnhancementOptions =
                        JsonConvert.DeserializeObject<ResourceCategoryEnhancementOptions>(c.EnhancementOptionsJson);
                }
                catch (Exception e)
                {

                }
            }

            return rc;
        }
    }
}