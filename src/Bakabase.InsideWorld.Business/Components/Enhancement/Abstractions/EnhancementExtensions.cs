using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.Abstractions.Extensions;
using Bakabase.InsideWorld.Business.Components.Enhancement.Abstractions.Models.Domain;
using Bakabase.InsideWorld.Business.Components.StandardValue;
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
                TargetOptionsMap = string.IsNullOrEmpty(ce.TargetPropertyIdMap) ? null : JsonConvert.DeserializeObject<Dictionary<int, EnhancerTargetOptions>>(ce.TargetPropertyIdMap)
            };
        }

        public static Models.Domain.Enhancement? ToDomainModel(this Models.Db.Enhancement? dbModel)
        {
            if (dbModel == null)
            {
                return null;
            }

            return new Models.Domain.Enhancement
            {
                CreatedAt = dbModel.CreatedAt,
                EnhancerId = dbModel.EnhancerId,
                Id = dbModel.Id,
                ResourceId = dbModel.ResourceId,
                Target = dbModel.Target,
                Value = dbModel.Value?.DeserializeAsStandardValue(dbModel.ValueType),
                ValueType = dbModel.ValueType
            };
        }

        public static Models.Db.Enhancement? ToDbModel(this Models.Domain.Enhancement? domainModel)
        {
            if (domainModel == null)
            {
                return null;
            }

            return new Models.Db.Enhancement
            {
                CreatedAt = domainModel.CreatedAt,
                EnhancerId = domainModel.EnhancerId,
                Id = domainModel.Id,
                ResourceId = domainModel.ResourceId,
                Target = domainModel.Target,
                Value = domainModel.Value.Serialize(),
                ValueType = domainModel.ValueType
            };
        }
    }
}
