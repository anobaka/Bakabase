using Bakabase.Abstractions.Models.Input;
using Bakabase.InsideWorld.Models.Constants.Aos;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.Abstractions.Extensions
{
    public static class ResourceExtensions
    {
        /// <summary>
        /// Gets a property value from a resource based on PropertyPool, PropertyId, and optional ValueScope.
        /// For Internal properties, values are read directly from Resource fields.
        /// For Reserved/Custom properties, values are read from the Properties dictionary.
        /// </summary>
        /// <param name="resource">The resource to get the value from</param>
        /// <param name="propertyPool">The property pool (Internal, Reserved, Custom)</param>
        /// <param name="propertyId">The property ID</param>
        /// <param name="valueScope">Optional value scope filter</param>
        /// <returns>A tuple of (DbValue, BizValue).</returns>
        public static (object? DbValue, object? BizValue) GetPropertyValue(
            this Resource resource,
            PropertyPool propertyPool,
            int propertyId,
            PropertyValueScope? valueScope = null)
        {
            if (propertyPool == PropertyPool.Internal)
            {
                return GetInternalPropertyValue(resource, propertyId);
            }

            return GetStoredPropertyValue(resource, propertyPool, propertyId, valueScope);
        }

        /// <summary>
        /// Gets an internal property value directly from Resource fields.
        /// Returns (DbValue, BizValue) tuple.
        /// </summary>
        private static (object? DbValue, object? BizValue) GetInternalPropertyValue(Resource resource, int propertyId)
        {
            var internalProperty = (InternalProperty)propertyId;
            return internalProperty switch
            {
                InternalProperty.Filename => (resource.FileName, resource.FileName),
                InternalProperty.DirectoryPath => (resource.Directory, resource.Directory),
                InternalProperty.CreatedAt => (resource.CreatedAt, resource.CreatedAt),
                InternalProperty.FileCreatedAt => (resource.FileCreatedAt, resource.FileCreatedAt),
                InternalProperty.FileModifiedAt => (resource.FileModifiedAt, resource.FileModifiedAt),
                InternalProperty.Category => (resource.CategoryId.ToString(), resource.Category?.Name),
                InternalProperty.MediaLibrary => (
                    new List<string> { resource.MediaLibraryId.ToString() },
                    new List<string?> { resource.MediaLibraryName }
                ),
                InternalProperty.MediaLibraryV2 => (
                    (resource.CategoryId == 0 ? resource.MediaLibraryId : -1).ToString(),
                    resource.MediaLibraryName
                ),
                InternalProperty.MediaLibraryV2Multi => (
                    resource.MediaLibraries?.Select(ml => ml.Id.ToString()).ToList(),
                    resource.MediaLibraries?.Select(ml => ml.Name).ToList()
                ),
                InternalProperty.ParentResource => (resource.ParentId?.ToString(), resource.Parent?.FileName),
                InternalProperty.PlayedAt => (resource.PlayedAt, resource.PlayedAt),
                _ => (null, null)
            };
        }

        /// <summary>
        /// Gets a property value from the Properties dictionary (for Reserved/Custom properties).
        /// </summary>
        private static (object? DbValue, object? BizValue) GetStoredPropertyValue(
            Resource resource,
            PropertyPool propertyPool,
            int propertyId,
            PropertyValueScope? valueScope)
        {
            if (resource.Properties == null)
                return (null, null);

            var poolKey = (int)propertyPool;
            if (!resource.Properties.TryGetValue(poolKey, out var poolProperties))
                return (null, null);

            if (!poolProperties.TryGetValue(propertyId, out var property))
                return (null, null);

            if (property.Values == null || property.Values.Count == 0)
                return (null, null);

            if (valueScope.HasValue)
            {
                var scopeValue = property.Values.FirstOrDefault(v => v.Scope == (int)valueScope.Value);
                return (scopeValue?.Value, scopeValue?.BizValue);
            }

            var firstValue = property.Values.FirstOrDefault();
            return (firstValue?.Value, firstValue?.BizValue);
        }

        public static (Func<ResourceDbModel, object> SelectKey, bool Asc, IComparer<object>? Comparer)[] BuildForSearch(
            this ResourceSearchOrderInputModel[]? orders)
        {
            var ordersForSearch =
                new List<(Func<ResourceDbModel, object> SelectKey, bool Asc, IComparer<object>? Comparer)>();
            if (orders != null)
            {
                ordersForSearch.AddRange(from om in orders
                    let o = om.Property
                    let a = om.Asc
                    let s = ((Func<ResourceDbModel, object> SelectKey, IComparer<object>? Comparer)) (o switch
                    {
                        ResourceSearchSortableProperty.AddDt => (x => x.CreateDt, null),
                        // ResourceSearchSortableProperty.Category => (x => x.CategoryId, null),
                        // ResourceSearchSortableProperty.MediaLibrary => (x => x.MediaLibraryId, null),
                        ResourceSearchSortableProperty.FileCreateDt => (x => x.FileCreateDt, null),
                        ResourceSearchSortableProperty.FileModifyDt => (x => x.FileModifyDt, null),
                        ResourceSearchSortableProperty.Filename => (x => Path.GetFileName(x.Path),
                            Comparer<object>.Create(StringComparer.OrdinalIgnoreCase.Compare)),
                        // ResourceSearchSortableProperty.ReleaseDt => (x => x.re),
                        ResourceSearchSortableProperty.PlayedAt => (x => x.PlayedAt, null),
                        _ => throw new ArgumentOutOfRangeException()
                    })
                    select (s.SelectKey, a, s.Comparer));
            }

            if (!ordersForSearch.Any())
            {
                ordersForSearch.Add((t => (t.Tags & ResourceTag.Pinned) == ResourceTag.Pinned, false, null));
                ordersForSearch.Add((t => t.Id, false, null));
            }

            return ordersForSearch.ToArray();
        }

        public static ResourceDiffDbModel ToDbModel(this ResourceDiff domainModel)
        {
            return new ResourceDiffDbModel
            {
                PropertyId = domainModel.PropertyId,
                PropertyPool = domainModel.PropertyPool,
                Value1 = domainModel.SerializedValue1,
                Value2 = domainModel.SerializedValue2
            };
        }

        public static ResourceDiff ToDomainModel(this ResourceDiffDbModel dbModel, Func<string?, object?> deserialize)
        {
            return new ResourceDiff
            {
                PropertyId = dbModel.PropertyId,
                PropertyPool = dbModel.PropertyPool,
                Value1 = deserialize(dbModel.Value1),
                SerializedValue1 = dbModel.Value1,
                Value2 = deserialize(dbModel.Value2),
                SerializedValue2 = dbModel.Value2,
            };
        }

        public static Dictionary<int, Dictionary<int, Resource.Property>> Copy(
            this Dictionary<int, Dictionary<int, Resource.Property>> properties)
        {
            return properties.ToDictionary(
                x => x.Key,
                x => x.Value.ToDictionary(
                    y => y.Key,
                    y => new Resource.Property(
                        y.Value.Name,
                        y.Value.Type,
                        y.Value.DbValueType,
                        y.Value.BizValueType,
                        y.Value.Values?.Select(z => new Resource.Property.PropertyValue(
                            z.Scope,
                            z.Value,
                            z.BizValue,
                            z.AliasAppliedBizValue
                        )).ToList(),
                        y.Value.Visible,
                        y.Value.Order
                    )
                )
            );
        }
    }
}