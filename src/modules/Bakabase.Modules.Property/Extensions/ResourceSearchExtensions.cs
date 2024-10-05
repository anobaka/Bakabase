using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.Abstractions.Exceptions;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.Property.Components;
using Bakabase.Modules.Property.Models.Db;
using Bakabase.Modules.StandardValue.Abstractions.Configurations;
using Bakabase.Modules.StandardValue.Extensions;

namespace Bakabase.Modules.Property.Extensions
{
    public static class ResourceSearchExtensions
    {
        public static PropertyType? GetPropertyType(this ResourceSearchFilter filter) =>
            filter.PropertyPool == PropertyPool.Custom
                ? filter.Property?.Type
                : PropertyInternals.BuiltinPropertyMap.GetValueOrDefault((ResourceProperty) filter.PropertyId)?.Type;

        public static bool IsValid(this ResourceSearchFilter filter)
        {
            if (filter is {PropertyPool: PropertyPool.Custom, Property: null})
            {
                return false;
            }

            var propertyType = filter.PropertyPool == PropertyPool.Custom
                ? filter.Property!.Type
                : PropertyInternals.BuiltinPropertyMap.GetValueOrDefault((ResourceProperty) filter.PropertyId)?.Type;

            if (!propertyType.HasValue)
            {
                return false;
            }

            var psh = PropertyInternals.PropertySearchHandlerMap.GetValueOrDefault(propertyType.Value);
            var dbValueType =
                psh?.SearchOperations.GetValueOrDefault(filter.Operation)?.AsType.GetDbValueType();

            if (!dbValueType.HasValue)
            {
                return false;
            }

            var stdValueHandler = StandardValueInternals.HandlerMap.GetValueOrDefault(dbValueType.Value);
            if (stdValueHandler == null)
            {
                return false;
            }

            var optimizedDbValue = stdValueHandler.Optimize(filter.DbValue);

            if (filter.Operation is not SearchOperation.IsNull and not SearchOperation.IsNotNull &&
                optimizedDbValue == null)
            {
                return false;
            }

            return true;
        }

        public static ResourceSearchFilterGroup? Optimize(this ResourceSearchFilterGroup group)
        {
            var validFilters = group.Filters?.Where(f => f.IsValid()).ToList();
            var validGroups = group.Groups?.Select(g => g.Optimize()).OfType<ResourceSearchFilterGroup>().ToList();
            if (validFilters?.Any() == true || validGroups?.Any() == true)
            {
                return group with
                {
                    Filters = validFilters,
                    Groups = validGroups
                };
            }

            return null;
        }

        public static ResourceSearchFilterDbModel ToDbModel(this ResourceSearchFilter model)
        {
            if (model is {PropertyPool: PropertyPool.Custom, Property: null})
            {
                throw new DevException(
                    $"For filter of custom property, {nameof(ResourceSearchFilter.Property)} is required to convert {nameof(ResourceSearchFilter)} to {nameof(ResourceSearchFilterDbModel)}");
            }

            var dbModel = new ResourceSearchFilterDbModel
            {
                PropertyId = model.PropertyId,
                PropertyPool = model.PropertyPool,
                Operation = model.Operation,
                // Value = dbValueType.HasValue ? model.DbValue?.SerializeAsStandardValue(dbValueType.Value) : null,
                // ValueType = dbValueType ?? default
            };

            var propertyType = model.Property?.Type ?? PropertyInternals.BuiltinPropertyMap
                .GetValueOrDefault((ResourceProperty) model.PropertyId)?.Type;

            if (propertyType.HasValue)
            {
                var psh = PropertyInternals.PropertySearchHandlerMap.GetValueOrDefault(propertyType.Value);
                var valueType = psh?.SearchOperations.GetValueOrDefault(model.Operation)?.AsType.GetDbValueType();
                if (valueType.HasValue)
                {
                    dbModel.ValueType = valueType.Value;
                    dbModel.Value = model.DbValue?.SerializeAsStandardValue(valueType.Value);
                }
            }

            return dbModel;
        }

        public static ResourceSearchFilterGroupDbModel ToDbModel(this ResourceSearchFilterGroup model)
        {
            return new ResourceSearchFilterGroupDbModel
            {
                Filters = model.Filters?.Select(f => f.ToDbModel()).ToList(),
                Groups = model.Groups?.Select(g => g.ToDbModel()).ToList(),
                Combinator = model.Combinator
            };
        }

        public static ResourceSearchDbModel ToDbModel(this ResourceSearch model)
        {
            return new ResourceSearchDbModel
            {
                Keyword = model.Keyword,
                Group = model.Group?.ToDbModel(),
                Orders = model.Orders,
                PageIndex = model.PageIndex,
                PageSize = model.PageSize
            };
        }

        public static ResourceSearchFilterGroup ToDomainModel(this ResourceSearchFilterGroupDbModel model)
        {
            return new ResourceSearchFilterGroup
            {
                Combinator = model.Combinator,
                Groups = model.Groups?.Select(g => g.ToDomainModel()).ToList(),
                Filters = model.Filters?.Select(f => f.ToDomainModel()).ToList()
            };
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="model"></param>
        /// <returns>
        /// <see cref="ResourceSearchFilter.Property"/> will not be populated.
        /// </returns>
        public static ResourceSearchFilter ToDomainModel(this ResourceSearchFilterDbModel model)
        {
            return new ResourceSearchFilter
            {
                Operation = model.Operation,
                PropertyId = model.PropertyId,
                PropertyPool = model.PropertyPool,
                DbValue = model.Value?.DeserializeAsStandardValue(model.ValueType)
            };
        }

        public static ResourceSearch ToDomainModel(this ResourceSearchDbModel model)
        {
            return new ResourceSearch
            {
                Group = model.Group?.ToDomainModel(),
                Orders = model.Orders,
                Keyword = model.Keyword,
                PageIndex = model.PageIndex,
                PageSize = model.PageSize
            };
        }

        public static Bakabase.Abstractions.Models.Domain.Property? TryGetProperty(this ResourceSearchFilter filter)
        {
            return filter.Property == null && filter.PropertyPool != PropertyPool.Custom
                ? PropertyInternals.BuiltinPropertyMap.GetValueOrDefault((ResourceProperty) filter.PropertyId)
                : filter.Property;
        }
    }
}