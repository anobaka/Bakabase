using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Input;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Constants.Aos;

namespace Bakabase.Abstractions.Extensions
{
    public static class ResourceExtensions
    {
        /// <summary>
        /// The name a resource is known by when it has no filename to be known by — a work the user
        /// has not downloaded yet, an uninstalled game. Reads the reserved <c>Name</c> property, so
        /// it is only populated on a resource loaded with its properties.
        /// </summary>
        public static string? GetReservedName(this Resource resource) =>
            resource.Properties?.GetValueOrDefault((int) PropertyPool.Reserved)
                ?.GetValueOrDefault((int) ReservedProperty.Name)?.Values
                ?.Select(v => v.BizValue as string)
                .FirstOrDefault(v => !string.IsNullOrEmpty(v));

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
                InternalProperty.MediaLibraryV2 => (resource.MediaLibraryId.ToString(), resource.MediaLibraryName),
                InternalProperty.MediaLibraryV2Multi => (
                    resource.MediaLibraries?.Select(ml => ml.Id.ToString()).ToList(),
                    resource.MediaLibraries?.Select(ml => ml.Name).ToList()
                ),
                InternalProperty.ParentResource => (resource.ParentId?.ToString(), resource.Parent?.FileName),
                InternalProperty.PlayedAt => (resource.PlayedAt, resource.PlayedAt),
                InternalProperty.Source => (
                    resource.SourceLinks?.Select(l => ((int)l.Source).ToString()).Distinct().ToList(),
                    resource.SourceLinks?.Select(l => l.Source.ToString()).Distinct().ToList()
                ),
                InternalProperty.HasLocalPath => (resource.HasLocalPath, resource.HasLocalPath),
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
            this ResourceSearchOrderInputModel[]? orders,
            IResourceHealthScoreReader? healthScoreReader = null)
        {
            var ordersForSearch =
                new List<(Func<ResourceDbModel, object> SelectKey, bool Asc, IComparer<object>? Comparer)>();
            if (orders != null)
            {
                foreach (var om in orders)
                {
                    var o = om.Property;
                    var a = om.Asc;
                    Func<ResourceDbModel, object> selectKey;
                    IComparer<object>? comparer = null;
                    switch (o)
                    {
                        case ResourceSearchSortableProperty.AddDt:
                            selectKey = x => x.CreateDt;
                            break;
                        case ResourceSearchSortableProperty.FileCreateDt:
                            // A resource with no local files has no file time — the column holds
                            // the moment its row was written. Sink those to the end whichever way
                            // the sort runs, the same way unscored resources sink below.
                            var fileCreateSentinel = a ? DateTime.MaxValue : DateTime.MinValue;
                            selectKey = x => string.IsNullOrEmpty(x.Path) ? fileCreateSentinel : x.FileCreateDt;
                            break;
                        case ResourceSearchSortableProperty.FileModifyDt:
                            var fileModifySentinel = a ? DateTime.MaxValue : DateTime.MinValue;
                            selectKey = x => string.IsNullOrEmpty(x.Path) ? fileModifySentinel : x.FileModifyDt;
                            break;
                        case ResourceSearchSortableProperty.Filename:
                            selectKey = x => Path.GetFileName(x.Path) ?? string.Empty;
                            comparer = BuildEmptyLastComparer(a);
                            break;
                        case ResourceSearchSortableProperty.PlayedAt:
                            selectKey = x => x.PlayedAt;
                            break;
                        case ResourceSearchSortableProperty.HealthScore:
                            // Unscored resources sink to the end regardless of sort
                            // direction: ascending uses MaxValue as a sentinel, descending
                            // uses MinValue.
                            var sentinel = a ? decimal.MaxValue : decimal.MinValue;
                            selectKey = x => healthScoreReader?.GetAggregatedScore(x.Id) ?? sentinel;
                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                    ordersForSearch.Add((selectKey, a, comparer));
                }
            }

            if (!ordersForSearch.Any())
            {
                ordersForSearch.Add((t => (t.Tags & ResourceTag.Pinned) == ResourceTag.Pinned, false, null));
                ordersForSearch.Add((t => t.Id, false, null));
            }

            return ordersForSearch.ToArray();
        }

        /// <summary>
        /// Orders strings case-insensitively but always puts the empty ones last, whichever
        /// direction the caller asked for. A resource with no local files has no filename, and
        /// a block of blanks at the top of the list reads as a bug.
        /// </summary>
        private static IComparer<object> BuildEmptyLastComparer(bool asc) =>
            Comparer<object>.Create((l, r) =>
            {
                var left = l as string;
                var right = r as string;
                var leftEmpty = string.IsNullOrEmpty(left);
                var rightEmpty = string.IsNullOrEmpty(right);

                if (leftEmpty || rightEmpty)
                {
                    if (leftEmpty && rightEmpty) return 0;
                    // The caller flips the comparison for a descending sort, so pre-flip here to
                    // keep the empties at the end either way.
                    return (leftEmpty ? 1 : -1) * (asc ? 1 : -1);
                }

                return StringComparer.OrdinalIgnoreCase.Compare(left, right);
            });

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

        /// <summary>
        /// Sets the resource path by combining directory and fileName.
        /// Use this when you need to build a path from separate components.
        /// </summary>
        public static void SetPath(this Resource resource, string? directory, string? fileName)
        {
            if (!string.IsNullOrEmpty(fileName) && !string.IsNullOrEmpty(directory))
            {
                resource.Path = System.IO.Path.Combine(directory, fileName).StandardizePath()!;
            }
            else if (!string.IsNullOrEmpty(directory))
            {
                resource.Path = directory.StandardizePath()!;
            }
            else if (!string.IsNullOrEmpty(fileName))
            {
                resource.Path = fileName;
            }
            else
            {
                resource.Path = null;
            }
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