using Bakabase.Abstractions.Models.Domain;
using Bakabase.Modules.Enhancer.Abstractions.Attributes;
using Bakabase.Modules.Enhancer.Abstractions.Models.Domain;
using Bakabase.Modules.Enhancer.Models.Domain;
using Bakabase.Modules.Enhancer.Models.Domain.Constants;
using Bakabase.Modules.StandardValue.Helpers;
using Bootstrap.Extensions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bakabase.Modules.Enhancer.Extensions;

public static class EnhancementExtensions
{
    public static CategoryEnhancerFullOptions? ToDomainModel(
        this Bakabase.Abstractions.Models.Db.CategoryEnhancerOptions? ce)
    {
        if (ce == null)
        {
            return null;
        }

        var model = new CategoryEnhancerFullOptions
        {
            Id = ce.Id,
            CategoryId = ce.CategoryId,
            EnhancerId = ce.EnhancerId,
            Active = ce.Active
        };

        if (!string.IsNullOrEmpty(ce.Options))
        {
            // var enhancerId = (EnhancerId) ce.EnhancerId;
            // var enhancerAttr = enhancerId.GetAttribute<EnhancerAttribute>();
            // var optionsType = enhancerAttr.OptionsType;
            // var jo = JObject.Parse(ce.Options);
            // var targetOptionsMapJo = jo[nameof(EnhancerOptions.TargetOptionsMap)];
            // jo.Remove(nameof(EnhancerOptions.TargetOptionsMap));
            // var options = (jo.ToObject(optionsType) as EnhancerOptions)!;
            // options.TargetOptionsMap = [];
            //
            // var targetEnumType = enhancerAttr.TargetEnumType;
            // var targetEnumValues = Enum.GetValues(targetEnumType);
            // foreach (var target in targetEnumValues)
            // {
            //     var targetOptionsJo = targetOptionsMapJo?[(int) target];
            //     if (targetOptionsJo != null)
            //     {
            //         var targetOptions = (targetOptionsJo.ToObject(enhancerAttr.OptionsType) as EnhancerTargetOptions)!;
            //         options.TargetOptionsMap.Add((int) target, targetOptions);
            //     }
            // }

            model.Options = JsonConvert.DeserializeObject<EnhancerFullOptions>(ce.Options);
        }

        return model;
    }
    public static Bakabase.Abstractions.Models.Db.CategoryEnhancerOptions ToDbModel(
        this CategoryEnhancerFullOptions ce)
    {
        var model = new Bakabase.Abstractions.Models.Db.CategoryEnhancerOptions
        {
            Id = ce.Id,
            CategoryId = ce.CategoryId,
            EnhancerId = ce.EnhancerId,
            Active = ce.Active,
            Options = JsonConvert.SerializeObject(ce.Options)
        };

        return model;
    }

    public static Enhancement? ToDomainModel(this Bakabase.Abstractions.Models.Db.Enhancement? dbModel)
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
            ValueType = dbModel.ValueType,
            CustomPropertyValueId = dbModel.CustomPropertyValueId
        };
    }

    public static Bakabase.Abstractions.Models.Db.Enhancement? ToDbModel(this Enhancement? domainModel)
    {
        if (domainModel == null)
        {
            return null;
        }

        return new Bakabase.Abstractions.Models.Db.Enhancement
        {
            CreatedAt = domainModel.CreatedAt,
            EnhancerId = domainModel.EnhancerId,
            Id = domainModel.Id,
            ResourceId = domainModel.ResourceId,
            Target = domainModel.Target,
            Value = domainModel.Value.SerializeAsStandardValue(),
            ValueType = domainModel.ValueType,
            CustomPropertyValueId = domainModel.CustomPropertyValueId
        };
    }
}