using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.Comparison.Models.Db;
using Bakabase.Modules.Comparison.Models.View;
using Bakabase.Modules.Property.Abstractions.Components;
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.Modules.Search.Extensions;
using Bakabase.Modules.Search.Models.Db;
using Bakabase.Service.Models.Input;
using Bootstrap.Extensions;
using ModuleInputModel = Bakabase.Modules.Comparison.Models.Input;
using ServiceViewModel = Bakabase.Service.Models.View;

namespace Bakabase.Service.Extensions;

public static class ComparisonExtensions
{
    #region InputModel Conversions

    public static async Task<ModuleInputModel.ComparisonPlanCreateInputModel> ToModuleInputModel(
        this ComparisonPlanCreateInputModel input,
        IPropertyService propertyService)
    {
        return new ModuleInputModel.ComparisonPlanCreateInputModel
        {
            Name = input.Name,
            Search = input.Search != null ? await input.Search.ToDomainModel(propertyService) : null,
            Threshold = input.Threshold,
            Rules = input.Rules
        };
    }

    public static async Task<ModuleInputModel.ComparisonPlanPatchInputModel> ToModuleInputModel(
        this ComparisonPlanPatchInputModel input,
        IPropertyService propertyService)
    {
        return new ModuleInputModel.ComparisonPlanPatchInputModel
        {
            Name = input.Name,
            Search = input.Search != null ? await input.Search.ToDomainModel(propertyService) : null,
            Threshold = input.Threshold,
            Rules = input.Rules
        };
    }

    #endregion

    #region ViewModel Conversions

    public static async Task<ServiceViewModel.ComparisonPlanViewModel> ToServiceViewModel(
        this ComparisonPlanDbModel db,
        List<ComparisonRuleViewModel> rules,
        IPropertyService propertyService,
        IPropertyLocalizer propertyLocalizer,
        IResourceService resourceService,
        int? groupCount = null)
    {
        ServiceViewModel.ResourceSearchViewModel? search = null;
        if (!string.IsNullOrEmpty(db.SearchJson))
        {
            var searchDbModel = db.SearchJson.JsonDeserializeOrDefault<ResourceSearchDbModel>();
            if (searchDbModel != null)
            {
                var searchDomainModel = await searchDbModel.ToDomainModel(propertyService);
                search = await searchDomainModel.ToViewModelAsync(propertyService, propertyLocalizer, resourceService);
            }
        }

        // Populate PropertyName for each rule
        foreach (var rule in rules)
        {
            try
            {
                var property = await propertyService.GetProperty(rule.PropertyPool, rule.PropertyId);
                if (property != null)
                {
                    // For builtin (Internal/Reserved) properties, use localized name
                    if (rule.PropertyPool == PropertyPool.Internal || rule.PropertyPool == PropertyPool.Reserved)
                    {
                        var builtinProperty = (ResourceProperty)rule.PropertyId;
                        rule.PropertyName = propertyLocalizer.BuiltinPropertyName(builtinProperty);
                    }
                    else
                    {
                        // For custom properties, use the property name directly
                        rule.PropertyName = property.Name;
                    }
                }
            }
            catch
            {
                // Ignore errors when property is not found
            }
        }

        return new ServiceViewModel.ComparisonPlanViewModel
        {
            Id = db.Id,
            Name = db.Name,
            Search = search,
            Threshold = db.Threshold,
            Rules = rules,
            CreatedAt = db.CreatedAt,
            LastRunAt = db.LastRunAt,
            ResultGroupCount = groupCount
        };
    }

    #endregion
}
