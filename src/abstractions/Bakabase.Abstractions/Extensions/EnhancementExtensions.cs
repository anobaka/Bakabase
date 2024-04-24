using Bakabase.Abstractions.Models.Domain;
using Newtonsoft.Json;

namespace Bakabase.Abstractions.Extensions
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

        public static Enhancement? ToDomainModel(this Models.Db.Enhancement? dbModel)
        {
            if (dbModel == null)
            {
                return null;
            }

            return new Enhancement
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

        public static Models.Db.Enhancement? ToDbModel(this Enhancement? domainModel)
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
