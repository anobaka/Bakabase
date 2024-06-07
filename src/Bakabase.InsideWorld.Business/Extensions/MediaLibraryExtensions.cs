
using Bakabase.InsideWorld.Business.Models.Domain;
using Newtonsoft.Json;
using System.Collections.Generic;
using System;
using System.Linq;
using Bakabase.Abstractions.Models.Domain;
using CsQuery.Utility;

namespace Bakabase.InsideWorld.Business.Extensions
{
    public static class MediaLibraryExtensions
    {
        public static MediaLibrary Duplicate(this MediaLibrary ml, int toCategoryId)
        {
            return ml with
            {
                Id = 0,
                CategoryId = toCategoryId,
                ResourceCount = 0,
            };
        }

        private static string? Serialize(this IEnumerable<PathConfiguration>? pcs)
        {
            if (pcs != null)
            {
                try
                {
                    return JsonConvert.SerializeObject(pcs.Select(p => p with { }));
                }
                catch
                {
                    // ignored
                }
            }

            return null;
        }

        public static MediaLibrary? ToDomainModel(this Abstractions.Models.Db.MediaLibrary? ml)
        {
            if (ml == null)
            {
                return null;
            }

            var d = new MediaLibrary
            {
                CategoryId = ml.CategoryId,
                Name = ml.Name,
                Order = ml.Order,
                Id = ml.Id,
                ResourceCount = ml.ResourceCount
            };

            if (!string.IsNullOrEmpty(ml.PathConfigurationsJson))
            {
                try
                {
                    d.PathConfigurations = JsonConvert
                        .DeserializeObject<List<PathConfiguration>>(ml.PathConfigurationsJson)?.ToList();
                }
                catch (Exception)
                {
                    // ignored
                }
            }

            return d;
        }

        public static Abstractions.Models.Db.MediaLibrary? ToDbModel(this MediaLibrary? ml)
        {
            if (ml == null)
            {
                return null;
            }

            var entity = new Abstractions.Models.Db.MediaLibrary
            {
                Id = ml.Id,
                CategoryId = ml.CategoryId,
                ResourceCount = ml.ResourceCount,
                Name = ml.Name,
                Order = ml.Order,
                PathConfigurationsJson = ml.PathConfigurations.Serialize()
            };

            return entity;
        }
    }
}