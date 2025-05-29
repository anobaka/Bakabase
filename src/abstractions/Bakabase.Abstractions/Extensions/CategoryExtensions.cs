using Bakabase.Abstractions.Models.Domain;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Newtonsoft.Json;

namespace Bakabase.Abstractions.Extensions
{
    public static class CategoryExtensions
    {
        public static Category ToDomainModel(this Abstractions.Models.Db.CategoryDbModel c)
        {
            var rc = new Category
            {
                Color = c.Color,
                CreateDt = c.CreateDt,
                Id = c.Id,
                Name = c.Name,
                CoverSelectionOrder = c.CoverSelectionOrder,
                Order = c.Order,
                GenerateNfo = c.GenerateNfo,
                ResourceDisplayNameTemplate = c.ResourceDisplayNameTemplate
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

        public static Models.Db.CategoryDbModel ToDbModel(this Category c)
        {
            var rc = new Models.Db.CategoryDbModel
            {
                Color = c.Color,
                CreateDt = c.CreateDt,
                Id = c.Id,
                Name = c.Name,
                CoverSelectionOrder = c.CoverSelectionOrder,
                Order = c.Order,
                GenerateNfo = c.GenerateNfo,
                ResourceDisplayNameTemplate = c.ResourceDisplayNameTemplate,
            };

            if (c.EnhancementOptions != null)
            {
                rc.EnhancementOptionsJson = JsonConvert.SerializeObject(c.EnhancementOptions);
            }

            return rc;
        }

        public static Category Duplicate(this Category category, string name)
        {
            return category with
            {
                Name = name,
                Id = 0,
                CreateDt = DateTime.Now
            };
        }
    }
}