using Bakabase.Abstractions.Models.Input;
using Bakabase.InsideWorld.Models.Constants.Aos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Db;
using Bakabase.InsideWorld.Models.Extensions;

namespace Bakabase.Abstractions.Extensions
{
    public static class ResourceExtensions
    {
        public static (Func<Resource, object> SelectKey, bool Asc, IComparer<object>? Comparer)[] BuildForSearch(
            this ResourceSearchOrderInputModel[]? orders)
        {
            var ordersForSearch = new List<(Func<Resource, object> SelectKey, bool Asc, IComparer<object>? Comparer)>();
            if (orders != null)
            {
                ordersForSearch.AddRange(from om in orders
                    let o = om.Property
                    let a = om.Asc
                    let s = ((Func<Resource, object> SelectKey, IComparer<object>? Comparer)) (o switch
                    {
                        ResourceSearchSortableProperty.AddDt => (x => x.CreateDt, null),
                        ResourceSearchSortableProperty.ReleaseDt => (x => x.ReleaseDt, null),
                        // ResourceSearchSortableProperty.Rate => (x => x.Rate, null),
                        ResourceSearchSortableProperty.Category => (x => x.CategoryId, null),
                        ResourceSearchSortableProperty.MediaLibrary => (x => x.MediaLibraryId, null),
                        // ResourceSearchSortableProperty.Name => (x => x.Name,
                        // 	Comparer<object>.Create(StringComparer.OrdinalIgnoreCase.Compare)),
                        ResourceSearchSortableProperty.FileCreateDt => (x => x.FileCreateDt, null),
                        ResourceSearchSortableProperty.FileModifyDt => (x => x.FileModifyDt, null),
                        ResourceSearchSortableProperty.Filename => (x => x.RawName,
                            Comparer<object>.Create(StringComparer.OrdinalIgnoreCase.Compare)),
                        _ => throw new ArgumentOutOfRangeException()
                    })
                    select (s.SelectKey, a, s.Comparer));
            }

            if (!ordersForSearch.Any())
            {
                ordersForSearch.Add((t => t.Id, false, null));
            }

            return ordersForSearch.ToArray();
        }

        public static string BuildPath(this Resource r)
        {
            return Path
                .Combine(new[] {r.Directory, r.RawName}.Where(a => !string.IsNullOrEmpty(a)).ToArray())
                .StandardizePath()!;
        }
    }
}