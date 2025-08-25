using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Enhancer.Abstractions.Attributes;
using Bakabase.Modules.Enhancer.Abstractions.Components;
using Bakabase.Modules.Enhancer.Abstractions.Models.Domain;
using Bakabase.Modules.Enhancer.Models.Domain;
using Bakabase.Modules.Enhancer.Models.Domain.Constants;
using Bakabase.Modules.StandardValue.Extensions;
using Bootstrap.Extensions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bakabase.Modules.Enhancer.Extensions;

public static class EnhancementExtensions
{
    public static CategoryEnhancerFullOptions ToDomainModel(
        this Bakabase.Abstractions.Models.Db.CategoryEnhancerOptions ce, IEnhancerDescriptor? enhancer)
    {
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

        if (enhancer != null)
        {
            model.AddDefaultOptions(enhancer);
        }

        return model;
    }

    public static void AddDefaultOptions(this CategoryEnhancerFullOptions options, IEnhancerDescriptor enhancer)
    {
        foreach (var t in enhancer.Targets.Where(x => x.ReservedPropertyCandidate.HasValue))
        {
            options.Options ??= new();
            options.Options.TargetOptions ??= [];
            var to = options.Options.TargetOptions.FirstOrDefault(x => x.Target == t.Id);
            if (to == null)
            {
                options.Options.TargetOptions.Add(new EnhancerTargetFullOptions
                {
                    PropertyPool = PropertyPool.Reserved,
                    PropertyId = (int)t.ReservedPropertyCandidate!.Value,
                    Target = t.Id
                });
            }
        }
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
            Options = JsonConvert.SerializeObject(ce.Options),
        };

        return model;
    }

    public static Enhancement ToDomainModel(this Bakabase.Abstractions.Models.Db.EnhancementDbModel dbModel)
    {
        return new Enhancement
        {
            EnhancerId = dbModel.EnhancerId,
            Id = dbModel.Id,
            ResourceId = dbModel.ResourceId,
            Target = dbModel.Target,
            Value = dbModel.Value?.DeserializeAsStandardValue(dbModel.ValueType),
            ValueType = dbModel.ValueType,
            PropertyPool = dbModel.PropertyPool,
            PropertyId = dbModel.PropertyId,
            DynamicTarget = dbModel.DynamicTarget,
            Key = dbModel.Key
        };
    }

    public static Bakabase.Abstractions.Models.Db.EnhancementDbModel ToDbModel(this Enhancement domainModel)
    {
        var dbModel = new Bakabase.Abstractions.Models.Db.EnhancementDbModel
        {
            EnhancerId = domainModel.EnhancerId,
            Id = domainModel.Id,
            ResourceId = domainModel.ResourceId,
            Target = domainModel.Target,
            Value = domainModel.Value?.SerializeAsStandardValue(domainModel.ValueType),
            ValueType = domainModel.ValueType,
            PropertyPool = domainModel.PropertyPool,
            PropertyId = domainModel.PropertyId,
            DynamicTarget = domainModel.DynamicTarget
        };
        dbModel.FillKey();
        return dbModel;
    }
}