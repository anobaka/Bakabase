using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bootstrap.Extensions;

namespace Bakabase.InsideWorld.Business.Components.BulkModification.ExpressionBuilders.Infrastructures
{
    public abstract class BmAbstractFilterExpressionBuilder : IBulkModificationFilterExpressionBuilder
    {
        protected abstract BulkModificationProperty Property { get; }

        protected virtual HashSet<BulkModificationFilterOperation> NullableValueOperations { get; } =
            [BulkModificationFilterOperation.IsNotNull, BulkModificationFilterOperation.IsNull];

        public Expression<Func<ResourceDto, bool>> Build(BulkModificationFilter filter)
        {
            if (Property != filter.Property)
            {
                throw new Exception(
                    $"Current expression builder [{GetType().Name}] can't build expression with property: {filter.Property}, available property is: {Property}");
            }

            var attr = SpecificTypeUtils<BulkModificationProperty>.Type.GetField(filter.Property.ToString())!
                .GetCustomAttributes<BulkModificationPropertyFilterAttribute>(false).FirstOrDefault()!;

            if (!attr.AvailableOperations.Contains(filter.Operation))
            {
                throw new Exception(
                    $"Operation [{filter.Operation}] is not valid for current property [{filter.Property}], available operations are: {string.Join(',', attr.AvailableOperations.Select(o => o.ToString()))}");
            }

            if (!NullableValueOperations.Contains(filter.Operation) && string.IsNullOrEmpty(filter.Target))
            {
                throw new Exception($"Operation [{filter.Operation}] does not support null target value");
            }

            var func = filter.Operation switch
            {
                BulkModificationFilterOperation.Equals => BuildEquals(filter),
                BulkModificationFilterOperation.NotEquals => BuildNotEquals(filter),
                BulkModificationFilterOperation.Contains => BuildContains(filter),
                BulkModificationFilterOperation.NotContains => BuildNotContains(filter),
                BulkModificationFilterOperation.StartsWith => BuildStartsWith(filter),
                BulkModificationFilterOperation.NotStartsWith => BuildNotStartsWith(filter),
                BulkModificationFilterOperation.EndsWith => BuildEndsWith(filter),
                BulkModificationFilterOperation.NotEndsWith => BuildNotEndsWith(filter),
                BulkModificationFilterOperation.GreaterThan => BuildGreaterThan(filter),
                BulkModificationFilterOperation.LessThan => BuildLessThan(filter),
                BulkModificationFilterOperation.GreaterThanOrEquals => BuildGreaterThanOrEquals(filter),
                BulkModificationFilterOperation.LessThanOrEquals => BuildLessThanOrEquals(filter),
                BulkModificationFilterOperation.IsNull => BuildIsNull(filter),
                BulkModificationFilterOperation.IsNotNull => BuildIsNotNull(filter),
                BulkModificationFilterOperation.In => BuildIn(filter),
                BulkModificationFilterOperation.NotIn => BuildNotIn(filter),
                BulkModificationFilterOperation.Matches => BuildMatches(filter),
                BulkModificationFilterOperation.NotMatches => BuildNotMatches(filter),
                _ => throw new ArgumentOutOfRangeException()
            };

            return r => func(r);
        }

        protected abstract Func<ResourceDto, bool> BuildEquals(BulkModificationFilter filter);
        protected abstract Func<ResourceDto, bool> BuildNotEquals(BulkModificationFilter filter);
        protected abstract Func<ResourceDto, bool> BuildContains(BulkModificationFilter filter);
        protected abstract Func<ResourceDto, bool> BuildNotContains(BulkModificationFilter filter);
        protected abstract Func<ResourceDto, bool> BuildStartsWith(BulkModificationFilter filter);
        protected abstract Func<ResourceDto, bool> BuildNotStartsWith(BulkModificationFilter filter);
        protected abstract Func<ResourceDto, bool> BuildEndsWith(BulkModificationFilter filter);
        protected abstract Func<ResourceDto, bool> BuildNotEndsWith(BulkModificationFilter filter);
        protected abstract Func<ResourceDto, bool> BuildGreaterThan(BulkModificationFilter filter);
        protected abstract Func<ResourceDto, bool> BuildLessThan(BulkModificationFilter filter);
        protected abstract Func<ResourceDto, bool> BuildGreaterThanOrEquals(BulkModificationFilter filter);
        protected abstract Func<ResourceDto, bool> BuildLessThanOrEquals(BulkModificationFilter filter);
        protected abstract Func<ResourceDto, bool> BuildIsNull(BulkModificationFilter filter);
        protected abstract Func<ResourceDto, bool> BuildIsNotNull(BulkModificationFilter filter);
        protected abstract Func<ResourceDto, bool> BuildIn(BulkModificationFilter filter);
        protected abstract Func<ResourceDto, bool> BuildNotIn(BulkModificationFilter filter);
        protected abstract Func<ResourceDto, bool> BuildMatches(BulkModificationFilter filter);
        protected abstract Func<ResourceDto, bool> BuildNotMatches(BulkModificationFilter filter);
    }
}