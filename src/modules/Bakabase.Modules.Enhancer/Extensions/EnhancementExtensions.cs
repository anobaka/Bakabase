using Bakabase.Abstractions.Models.Domain;
using Bakabase.Modules.Enhancer.Abstractions.Attributes;
using Bakabase.Modules.Enhancer.Models.Domain;
using Bakabase.Modules.Enhancer.Models.Domain.Constants;
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
            // var targetOptionsMapJo = jo[nameof(EnhancerOptions.TargetFullOptionsMap)];
            // jo.Remove(nameof(EnhancerOptions.TargetFullOptionsMap));
            // var options = (jo.ToObject(optionsType) as EnhancerOptions)!;
            // options.TargetFullOptionsMap = [];
            //
            // var targetEnumType = enhancerAttr.TargetEnumType;
            // var targetEnumValues = Enum.GetValues(targetEnumType);
            // foreach (var target in targetEnumValues)
            // {
            //     var targetOptionsJo = targetOptionsMapJo?[(int) target];
            //     if (targetOptionsJo != null)
            //     {
            //         var targetOptions = (targetOptionsJo.ToObject(enhancerAttr.OptionsType) as EnhancerTargetOptions)!;
            //         options.TargetFullOptionsMap.Add((int) target, targetOptions);
            //     }
            // }

            model.FullOptions = JsonConvert.DeserializeObject<EnhancerFullOptions>(ce.Options);
        }

        return model;
    }
}