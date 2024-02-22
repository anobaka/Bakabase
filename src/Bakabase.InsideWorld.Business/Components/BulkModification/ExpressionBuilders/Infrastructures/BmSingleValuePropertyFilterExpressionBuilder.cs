using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Business.Components.BulkModification.ExpressionBuilders.Infrastructures
{
    /// <summary>
    /// 
    /// </summary>
    public abstract class BmSingleValuePropertyFilterExpressionBuilder<TValue> : BmAbstractFilterExpressionBuilder
    {
        protected abstract TValue? GetValue(ResourceDto resource);
        protected virtual string? ToString(TValue? value) => value?.ToString();
        private static bool IsComparable => typeof(IComparable).IsAssignableFrom(typeof(TValue));

        protected virtual Func<ResourceDto, bool> BuildEqualsOrNotEquals(BulkModificationFilter filter)
        {
            var target = JsonConvert.DeserializeObject<TValue>(filter.Target!)!;
            Func<ResourceDto, bool> func = filter.Operation switch
            {
                BulkModificationFilterOperation.Equals => r => GetValue(r)?.Equals(target) == true,
                BulkModificationFilterOperation.NotEquals => r => GetValue(r)?.Equals(target) != true,
                _ => throw new ArgumentOutOfRangeException()
            };
            return func;
        }

        protected override Func<ResourceDto, bool> BuildEquals(BulkModificationFilter filter) =>
            BuildEqualsOrNotEquals(filter);

        protected override Func<ResourceDto, bool> BuildNotEquals(BulkModificationFilter filter) =>
            BuildEqualsOrNotEquals(filter);

        protected virtual Func<ResourceDto, bool> BuildStringOperation(BulkModificationFilter filter)
        {
            var target = JsonConvert.DeserializeObject<string>(filter.Target!)!;
            Func<ResourceDto, bool> func = filter.Operation switch
            {
                BulkModificationFilterOperation.StartsWith => r =>
                    ToString(GetValue(r))?.StartsWith(target) == true,
                BulkModificationFilterOperation.NotStartsWith => r =>
                    ToString(GetValue(r))?.StartsWith(target) != true,
                BulkModificationFilterOperation.EndsWith => r =>
                    ToString(GetValue(r))?.EndsWith(target) == true,
                BulkModificationFilterOperation.NotEndsWith => r =>
                    ToString(GetValue(r))?.EndsWith(target) != true,
                BulkModificationFilterOperation.Matches => r =>
                {
                    var v = ToString(GetValue(r));
                    return !string.IsNullOrEmpty(v) && Regex.IsMatch(v, target);
                },
                BulkModificationFilterOperation.NotMatches => r =>
                {
                    var v = ToString(GetValue(r));
                    return string.IsNullOrEmpty(v) || !Regex.IsMatch(v, target);
                },
                BulkModificationFilterOperation.Contains => r =>
                    ToString(GetValue(r))?.Contains(target) == true,
                BulkModificationFilterOperation.NotContains => r =>
                    ToString(GetValue(r))?.Contains(target) != true,
                _ => throw new ArgumentOutOfRangeException()
            };
            return func;
        }

        protected override Func<ResourceDto, bool> BuildContains(BulkModificationFilter filter) =>
            BuildStringOperation(filter);

        protected override Func<ResourceDto, bool> BuildNotContains(BulkModificationFilter filter) =>
            BuildStringOperation(filter);

        protected override Func<ResourceDto, bool> BuildMatches(BulkModificationFilter filter) =>
            BuildStringOperation(filter);

        protected override Func<ResourceDto, bool> BuildNotMatches(BulkModificationFilter filter) =>
            BuildStringOperation(filter);

        protected override Func<ResourceDto, bool> BuildEndsWith(BulkModificationFilter filter) =>
            BuildStringOperation(filter);

        protected override Func<ResourceDto, bool> BuildNotEndsWith(BulkModificationFilter filter) =>
            BuildStringOperation(filter);

        protected override Func<ResourceDto, bool> BuildStartsWith(BulkModificationFilter filter) =>
            BuildStringOperation(filter);

        protected override Func<ResourceDto, bool> BuildNotStartsWith(BulkModificationFilter filter) =>
            BuildStringOperation(filter);

        protected virtual Func<ResourceDto, bool> BuildInOrNotIn(BulkModificationFilter filter)
        {
            var target = JsonConvert.DeserializeObject<HashSet<TValue>>(filter.Target!)!;
            Func<ResourceDto, bool> func = filter.Operation switch
            {
                BulkModificationFilterOperation.In => r =>
                {
                    var v = GetValue(r);
                    return v != null && target.Contains(v);
                },
                BulkModificationFilterOperation.NotIn => r =>
                {
                    var v = GetValue(r);
                    return v == null || target.Contains(v);
                },
                _ => throw new ArgumentOutOfRangeException()
            };
            return func;
        }

        protected override Func<ResourceDto, bool> BuildIn(BulkModificationFilter filter) => BuildInOrNotIn(filter);
        protected override Func<ResourceDto, bool> BuildNotIn(BulkModificationFilter filter) => BuildInOrNotIn(filter);

        protected virtual Func<ResourceDto, bool> BuildComparableOperation(BulkModificationFilter filter)
        {
            if (!IsComparable)
            {
                return r => true;
            }

            var target = JsonConvert.DeserializeObject<TValue>(filter.Target!)!;
            Func<ResourceDto, bool> func = filter.Operation switch
            {
                BulkModificationFilterOperation.GreaterThan => r =>
                {
                    var v = GetValue(r);
                    return v != null && ((IComparable) v).CompareTo(target) > 0;
                },
                BulkModificationFilterOperation.LessThan => r =>
                {
                    var v = GetValue(r);
                    return v != null && ((IComparable) v).CompareTo(target) < 0;
                },
                BulkModificationFilterOperation.GreaterThanOrEquals => r =>
                {
                    var v = GetValue(r);
                    return v != null && ((IComparable) v).CompareTo(target) >= 0;
                },
                BulkModificationFilterOperation.LessThanOrEquals => r =>
                {
                    var v = GetValue(r);
                    return v != null && ((IComparable) v).CompareTo(target) <= 0;
                },
                _ => throw new ArgumentOutOfRangeException()
            };
            return func;
        }

        protected override Func<ResourceDto, bool> BuildGreaterThan(BulkModificationFilter filter) =>
            BuildComparableOperation(filter);

        protected override Func<ResourceDto, bool> BuildGreaterThanOrEquals(BulkModificationFilter filter) =>
            BuildComparableOperation(filter);

        protected override Func<ResourceDto, bool> BuildLessThan(BulkModificationFilter filter) =>
            BuildComparableOperation(filter);

        protected override Func<ResourceDto, bool> BuildLessThanOrEquals(BulkModificationFilter filter) =>
            BuildComparableOperation(filter);

        protected override Func<ResourceDto, bool> BuildIsNull(BulkModificationFilter filter) =>
            r => GetValue(r) == null;

        protected override Func<ResourceDto, bool> BuildIsNotNull(BulkModificationFilter filter) =>
            r => GetValue(r) != null;
    }
}