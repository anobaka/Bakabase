using System.Collections.Generic;
using System.Linq;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.Property.Abstractions.Components;
using Bakabase.Modules.Property.Components;
using Bakabase.Modules.Property.Extensions;
using Bakabase.Modules.Property.Models.Db;
using Bakabase.Modules.StandardValue.Extensions;
using Bakabase.Service.Models.Input;
using Bakabase.Service.Models.View;
using Microsoft.EntityFrameworkCore.Scaffolding;

namespace Bakabase.Service.Extensions;

public static class ResourceSearchExtensions
{
    public static ResourceSearchFilterDbModel ToDbModel(this ResourceSearchFilterInputModel model)
    {
        return new ResourceSearchFilterDbModel
        {
            Value = model.DbValue,
            Operation = model.Operation,
            PropertyId = model.PropertyId,
            PropertyPool = model.PropertyPool,
            ValueType = model.ValueProperty!.Type.GetDbValueType()
        };
    }

    public static ResourceSearchFilterGroupDbModel ToDbModel(this ResourceSearchFilterGroupInputModel model)
    {
        return new ResourceSearchFilterGroupDbModel
        {
            Filters = model.Filters?.Select(f => f.ToDbModel()).ToList(),
            Groups = model.Groups?.Select(g => g.ToDbModel()).ToList(),
            Combinator = model.Combinator
        };
    }

    public static ResourceSearchDbModel ToDbModel(this ResourceSearchInputModel model)
    {
        return new ResourceSearchDbModel
        {
            Group = model.Group?.ToDbModel(),
            Keyword = model.Keyword,
            Orders = model.Orders,
            PageIndex = model.PageIndex,
            PageSize = model.PageSize
        };
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="filter"></param>
    /// <returns>
    /// <see cref="ResourceSearchFilter.Property"/> will not be populated.
    /// </returns>
    public static ResourceSearchFilter ToDomainModel(this ResourceSearchFilterInputModel filter)
    {
        var valueType = filter.ValueProperty?.Type.GetDbValueType();
        return new ResourceSearchFilter
        {
            DbValue = valueType.HasValue ? filter.DbValue?.DeserializeAsStandardValue(valueType.Value) : null,
            Operation = filter.Operation,
            PropertyId = filter.PropertyId,
            PropertyPool = filter.PropertyPool,
            Property = null
        };
    }

    public static ResourceSearchFilterGroup ToDomainModel(this ResourceSearchFilterGroupInputModel group)
    {
        return new ResourceSearchFilterGroup
        {
            Combinator = group.Combinator,
            Filters = group.Filters?.Select(x => x.ToDomainModel()).ToList(),
            Groups = group.Groups?.Select(g => g.ToDomainModel()).ToList()
        };
    }

    public static ResourceSearch ToDomainModel(this ResourceSearchInputModel model)
    {
        return new ResourceSearch
        {
            Group = model.Group?.ToDomainModel(),
            Keyword = model.Keyword,
            Orders = model.Orders,
            PageIndex = model.PageIndex,
            PageSize = model.PageSize,
        };
    }

    public static ResourceSearchViewModel ToViewModel(this ResourceSearch model,
        IPropertyLocalizer? propertyLocalizer = null)
    {
        return new ResourceSearchViewModel
        {
            Keyword = model.Keyword,
            Orders = model.Orders,
            PageIndex = model.PageIndex,
            PageSize = model.PageSize,
            Group = model.Group?.ToViewModel(propertyLocalizer)
        };
    }

    public static ResourceSearchFilterGroupViewModel ToViewModel(this ResourceSearchFilterGroup model,
        IPropertyLocalizer? propertyLocalizer = null)
    {
        return new ResourceSearchFilterGroupViewModel
        {
            Filters = model.Filters?.Select(f => f.ToViewModel(propertyLocalizer)).ToList(),
            Groups = model.Groups?.Select(g => g.ToViewModel()).ToList(),
            Combinator = model.Combinator
        };
    }

    public static ResourceSearchFilterViewModel ToViewModel(this ResourceSearchFilter model,
        IPropertyLocalizer? propertyLocalizer = null)
    {
        var filter = new ResourceSearchFilterViewModel
        {
            PropertyId = model.PropertyId,
            PropertyPool = model.PropertyPool,
            Operation = model.Operation,
        };

        var property = model.TryGetProperty();

        if (property != null)
        {
            var psh = PropertyInternals.PropertySearchHandlerMap.GetValueOrDefault(property.Type);
            if (psh != null)
            {
                filter.AvailableOperations = psh.SearchOperations.Keys.ToList();
                filter.Property = property.ToViewModel(propertyLocalizer);
                var convertProperty = psh.SearchOperations.GetValueOrDefault(filter.Operation)?.ConvertProperty;
                var valueProperty = property;
                if (convertProperty != null)
                {
                    valueProperty = convertProperty(valueProperty);
                }

                filter.ValueProperty = valueProperty.ToViewModel(propertyLocalizer);

                var asType = psh.SearchOperations.GetValueOrDefault(filter.Operation)?.AsType;
                if (asType.HasValue)
                {
                    filter.DbValue = model.DbValue?.SerializeAsStandardValue(asType.Value.GetDbValueType());
                    var pd = PropertyInternals.DescriptorMap.GetValueOrDefault(valueProperty.Type);
                    filter.BizValue = pd?.GetBizValue(valueProperty, model.DbValue)
                        ?.SerializeAsStandardValue(asType.Value.GetBizValueType());
                }
            }
        }

        return filter;
    }
}