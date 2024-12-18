using Bakabase.Abstractions.Models.Input;
using Bakabase.InsideWorld.Models.Constants.Aos;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Extensions
{
    public static class ResourceExtensions
    {
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
                        y.Value.DbValueType,
                        y.Value.BizValueType,
                        y.Value.Values?.Select(z => new Resource.Property.PropertyValue(
                            z.Scope,
                            z.Value,
                            z.BizValue,
                            z.AliasAppliedBizValue
                        )).ToList(),
                        y.Value.Visible
                    )
                )
            );
        }
    }
}