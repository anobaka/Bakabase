using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Extensions;
using Bakabase.InsideWorld.Models.Models.Dtos;

namespace Bakabase.InsideWorld.Models.Models.Aos
{
    public record ResourceDiff
    {
        public ResourceDiffProperty Property { get; set; }
        public object? CurrentValue { get; set; }
        public object? NewValue { get; set; }
        public ResourceDiffType Type { get; set; }
        public string? Key { get; set; }
        public List<ResourceDiff>? SubDiffs { get; set; }

        public static ResourceDiff Added(ResourceDiffProperty property, object? newValue) => new()
            {Type = ResourceDiffType.Added, NewValue = newValue, Property = property};

        public static ResourceDiff Removed(ResourceDiffProperty property, object? oldValue) => new()
            {Type = ResourceDiffType.Removed, CurrentValue = oldValue, Property = property};

        public static ResourceDiff? BuildRootDiffForArrayProperty<T>(ResourceDiffProperty property, List<T>? a, List<T>? b,
            IEqualityComparer<List<T>> equalityComparer,
            string? key,
            Func<List<T>, List<T>, List<ResourceDiff>?>? buildSubDiffs)
        {
            if (a?.Count == 0)
            {
                a = null;
            }

            if (b?.Count == 0)
            {
                b = null;
            }

            return BuildRootDiff(property, a, b, equalityComparer, key, buildSubDiffs);
        }

        public static ResourceDiff? BuildRootDiff<T>(ResourceDiffProperty property, T? a, T? b,
            IEqualityComparer<T> equalityComparer,
            string? key,
            Func<T, T, List<ResourceDiff>?>? buildSubDiffs)
        {
            if (a == null && b == null)
            {
                return null;
            }

            if (a == null && b != null)
            {
                return Added(property, b);
            }

            if (a != null && b == null)
            {
                return Removed(property, a);
            }

            var subDiffs = buildSubDiffs?.Invoke(a!, b!);

            if (subDiffs?.Any() != true && equalityComparer.Equals(a, b))
            {
                return null;
            }

            return new ResourceDiff
            {
                Property = property,
                CurrentValue = a,
                NewValue = b,
                Key = key,
                SubDiffs = subDiffs,
                Type = ResourceDiffType.Modified
            };
        }

        public static List<ResourceDiff>? BuildRootDiffs<T>(ResourceDiffProperty property,
            List<(T? A, T? B)>? pairs,
            IEqualityComparer<T> equalityComparer,
            string? key,
            Func<T, T, List<ResourceDiff>?>? buildSubDiffs) where T : class
        {
            if (pairs == null || !pairs.Any())
            {
                return null;
            }

            List<ResourceDiff>? diffs = null;
            foreach (var (ap, bp) in pairs)
            {
                var diff = BuildRootDiff(property, ap, bp, equalityComparer, key, buildSubDiffs);
                if (diff != null)
                {
                    diffs ??= new List<ResourceDiff>();
                    diffs.Add(diff);
                }
            }

            return diffs;
        }
    }
}