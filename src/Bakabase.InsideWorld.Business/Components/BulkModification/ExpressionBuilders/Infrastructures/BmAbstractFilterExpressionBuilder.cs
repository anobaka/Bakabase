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
using NPOI.OpenXml4Net.Util;

namespace Bakabase.InsideWorld.Business.Components.BulkModification.ExpressionBuilders.Infrastructures
{
    public abstract class BmAbstractFilterExpressionBuilder : IBulkModificationFilterExpressionBuilder
    {
        protected abstract BulkModificationFilterableProperty Property { get; }

        protected virtual HashSet<BulkModificationFilterOperation> NullableValueOperations { get; } =
            [BulkModificationFilterOperation.IsNotNull, BulkModificationFilterOperation.IsNull];

        public Expression<Func<Models.Domain.Resource, bool>> Build(BulkModificationFilter filter)
        {
            if (Property != filter.Property)
            {
                throw new Exception(
                    $"Current expression builder [{GetType().Name}] can't build expression with property: {filter.Property}, available property is: {Property}");
            }

            var attr = SpecificTypeUtils<BulkModificationFilterableProperty>.Type.GetField(filter.Property.ToString())!
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

        protected abstract Func<Models.Domain.Resource, bool> BuildEquals(BulkModificationFilter filter);
        protected abstract Func<Models.Domain.Resource, bool> BuildNotEquals(BulkModificationFilter filter);
        protected abstract Func<Models.Domain.Resource, bool> BuildContains(BulkModificationFilter filter);
        protected abstract Func<Models.Domain.Resource, bool> BuildNotContains(BulkModificationFilter filter);
        protected abstract Func<Models.Domain.Resource, bool> BuildStartsWith(BulkModificationFilter filter);
        protected abstract Func<Models.Domain.Resource, bool> BuildNotStartsWith(BulkModificationFilter filter);
        protected abstract Func<Models.Domain.Resource, bool> BuildEndsWith(BulkModificationFilter filter);
        protected abstract Func<Models.Domain.Resource, bool> BuildNotEndsWith(BulkModificationFilter filter);
        protected abstract Func<Models.Domain.Resource, bool> BuildGreaterThan(BulkModificationFilter filter);
        protected abstract Func<Models.Domain.Resource, bool> BuildLessThan(BulkModificationFilter filter);
        protected abstract Func<Models.Domain.Resource, bool> BuildGreaterThanOrEquals(BulkModificationFilter filter);
        protected abstract Func<Models.Domain.Resource, bool> BuildLessThanOrEquals(BulkModificationFilter filter);
        protected abstract Func<Models.Domain.Resource, bool> BuildIsNull(BulkModificationFilter filter);
        protected abstract Func<Models.Domain.Resource, bool> BuildIsNotNull(BulkModificationFilter filter);
        protected abstract Func<Models.Domain.Resource, bool> BuildIn(BulkModificationFilter filter);
        protected abstract Func<Models.Domain.Resource, bool> BuildNotIn(BulkModificationFilter filter);
        protected abstract Func<Models.Domain.Resource, bool> BuildMatches(BulkModificationFilter filter);
        protected abstract Func<Models.Domain.Resource, bool> BuildNotMatches(BulkModificationFilter filter);
    }
}