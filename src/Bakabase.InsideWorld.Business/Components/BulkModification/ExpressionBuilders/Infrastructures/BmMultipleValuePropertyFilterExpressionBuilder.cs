using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text.RegularExpressions;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Newtonsoft.Json;

namespace Bakabase.InsideWorld.Business.Components.BulkModification.ExpressionBuilders.Infrastructures
{
    public abstract class BmMultipleValuePropertyFilterExpressionBuilder<TValue> : BmAbstractFilterExpressionBuilder
    {
        protected abstract HashSet<TValue>? GetValue(Bakabase.Abstractions.Models.Domain.Resource resource, BulkModificationFilter filter);
        protected virtual string? ToString(TValue? value) => value?.ToString();

        protected virtual Func<Bakabase.Abstractions.Models.Domain.Resource, bool> BuildEqualsOrNotEquals(BulkModificationFilter filter)
        {
            var target = JsonConvert.DeserializeObject<HashSet<TValue>>(filter.Target!)!;
            Func<Bakabase.Abstractions.Models.Domain.Resource, bool> func = filter.Operation switch
            {
                BulkModificationFilterOperation.Equals => r =>
                {
                    var v = GetValue(r, filter);
                    return v != null && v.Count == target.Count && v.All(x => target.Contains(x));
                },
                BulkModificationFilterOperation.NotEquals => r =>
                {
                    var v = GetValue(r, filter);
                    return v == null || v.Count == target.Count || v.Any(x => !target.Contains(x));
                },
                _ => throw new ArgumentOutOfRangeException()
            };
            return func;
        }

        protected override Func<Bakabase.Abstractions.Models.Domain.Resource, bool> BuildEquals(BulkModificationFilter filter) =>
            BuildEqualsOrNotEquals(filter);

        protected override Func<Bakabase.Abstractions.Models.Domain.Resource, bool> BuildNotEquals(BulkModificationFilter filter) =>
            BuildEqualsOrNotEquals(filter);

        protected virtual Func<Bakabase.Abstractions.Models.Domain.Resource, bool> BuildStringOperation(BulkModificationFilter filter)
        {
            var target = JsonConvert.DeserializeObject<string>(filter.Target!)!;
            Func<Bakabase.Abstractions.Models.Domain.Resource, bool> func = filter.Operation switch
            {
                BulkModificationFilterOperation.StartsWith => r =>
                    GetValue(r, filter)?.Any(x => ToString(x)?.StartsWith(target) == true) == true,
                BulkModificationFilterOperation.NotStartsWith => r =>
                    GetValue(r, filter)?.Any(x => ToString(x)?.StartsWith(target) == true) != true,
                BulkModificationFilterOperation.EndsWith => r =>
                    GetValue(r, filter)?.Any(x => ToString(x)?.EndsWith(target) == true) == true,
                BulkModificationFilterOperation.NotEndsWith => r =>
                    GetValue(r, filter)?.Any(x => ToString(x)?.EndsWith(target) == true) != true,
                BulkModificationFilterOperation.Matches => r => GetValue(r, filter)?.Any(x =>
                {
                    var s = ToString(x);
                    return !string.IsNullOrEmpty(s) && Regex.IsMatch(s, target);
                }) == true,
                BulkModificationFilterOperation.NotMatches => r => GetValue(r, filter)?.Any(x =>
                {
                    var s = ToString(x);
                    return !string.IsNullOrEmpty(s) && Regex.IsMatch(s, target);
                }) != true,
                BulkModificationFilterOperation.Contains => r =>
                    GetValue(r, filter)?.Select(ToString).Contains(target) == true,
                BulkModificationFilterOperation.NotContains => r =>
                    GetValue(r, filter)?.Select(ToString).Contains(target) != true,
                _ => throw new ArgumentOutOfRangeException()
            };
            return func;
        }

        protected override Func<Bakabase.Abstractions.Models.Domain.Resource, bool> BuildContains(BulkModificationFilter filter) =>
            BuildStringOperation(filter);

        protected override Func<Bakabase.Abstractions.Models.Domain.Resource, bool> BuildNotContains(BulkModificationFilter filter) =>
            BuildStringOperation(filter);

        protected override Func<Bakabase.Abstractions.Models.Domain.Resource, bool> BuildMatches(BulkModificationFilter filter) =>
            BuildStringOperation(filter);

        protected override Func<Bakabase.Abstractions.Models.Domain.Resource, bool> BuildNotMatches(BulkModificationFilter filter) =>
            BuildStringOperation(filter);

        protected override Func<Bakabase.Abstractions.Models.Domain.Resource, bool> BuildEndsWith(BulkModificationFilter filter) =>
            BuildStringOperation(filter);

        protected override Func<Bakabase.Abstractions.Models.Domain.Resource, bool> BuildNotEndsWith(BulkModificationFilter filter) =>
            BuildStringOperation(filter);

        protected override Func<Bakabase.Abstractions.Models.Domain.Resource, bool> BuildStartsWith(BulkModificationFilter filter) =>
            BuildStringOperation(filter);

        protected override Func<Bakabase.Abstractions.Models.Domain.Resource, bool> BuildNotStartsWith(BulkModificationFilter filter) =>
            BuildStringOperation(filter);

        protected virtual Func<Bakabase.Abstractions.Models.Domain.Resource, bool> BuildInOrNotIn(BulkModificationFilter filter)
        {
            var target = JsonConvert.DeserializeObject<HashSet<TValue>>(filter.Target!)!;
            Func<Bakabase.Abstractions.Models.Domain.Resource, bool> func = filter.Operation switch
            {
                BulkModificationFilterOperation.In => r =>
                {
                    var v = GetValue(r, filter);
                    return v?.All(x => target.Contains(x)) == true;
                },
                BulkModificationFilterOperation.NotIn => r =>
                {
                    var v = GetValue(r, filter);
                    return v?.All(x => target.Contains(x)) != true;
                },
                _ => throw new ArgumentOutOfRangeException()
            };
            return func;
        }

        protected override Func<Bakabase.Abstractions.Models.Domain.Resource, bool> BuildIn(BulkModificationFilter filter) => BuildInOrNotIn(filter);
        protected override Func<Bakabase.Abstractions.Models.Domain.Resource, bool> BuildNotIn(BulkModificationFilter filter) => BuildInOrNotIn(filter);

        protected override Func<Bakabase.Abstractions.Models.Domain.Resource, bool> BuildIsNull(BulkModificationFilter filter) =>
            r => GetValue(r, filter)?.Any() != true;

        protected override Func<Bakabase.Abstractions.Models.Domain.Resource, bool> BuildIsNotNull(BulkModificationFilter filter) =>
            r => GetValue(r, filter)?.Any() == true;

        protected override Func<Bakabase.Abstractions.Models.Domain.Resource, bool> BuildGreaterThan(BulkModificationFilter filter)
        {
            throw new NotImplementedException();
        }

        protected override Func<Bakabase.Abstractions.Models.Domain.Resource, bool> BuildLessThan(BulkModificationFilter filter)
        {
            throw new NotImplementedException();
        }

        protected override Func<Bakabase.Abstractions.Models.Domain.Resource, bool> BuildGreaterThanOrEquals(BulkModificationFilter filter)
        {
            throw new NotImplementedException();
        }

        protected override Func<Bakabase.Abstractions.Models.Domain.Resource, bool> BuildLessThanOrEquals(BulkModificationFilter filter)
        {
            throw new NotImplementedException();
        }
    }
}