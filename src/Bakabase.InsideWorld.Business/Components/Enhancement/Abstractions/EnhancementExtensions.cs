using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Enhancement.Abstractions.Models.Domain;
using Newtonsoft.Json;

namespace Bakabase.InsideWorld.Business.Components.Enhancement.Abstractions
{
    public static class EnhancementExtensions
    {
        public static CategoryEnhancerOptions? ToDomainModel(this Models.Db.CategoryEnhancerOptions? ce)
        {
            if (ce == null)
            {
                return null;
            }

            return new CategoryEnhancerOptions
            {
                Id = ce.Id,
                CategoryId = ce.CategoryId,
                EnhancerId = ce.EnhancerId,
                TargetPropertyIdMap = string.IsNullOrEmpty(ce.TargetPropertyIdMap) ? null : JsonConvert.DeserializeObject<Dictionary<int, int>>(ce.TargetPropertyIdMap)
            };
        }
    }
}
