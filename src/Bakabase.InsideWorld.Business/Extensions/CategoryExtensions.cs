using Bakabase.InsideWorld.Business.Models.Domain;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bakabase.InsideWorld.Models.Models.Entities;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;

namespace Bakabase.InsideWorld.Business.Extensions
{
    public static class CategoryExtensions
    {
        public static Category ToDomainModel(this ResourceCategory c)
        {
            if (c == null)
            {
                return null;
            }

            var rc = new Category
            {
                Color = c.Color,
                CreateDt = c.CreateDt,
                Id = c.Id,
                Name = c.Name,
                CoverSelectionOrder = c.CoverSelectionOrder,
                Order = c.Order,
                GenerateNfo = c.GenerateNfo
            };

            if (!string.IsNullOrEmpty(c.EnhancementOptionsJson))
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