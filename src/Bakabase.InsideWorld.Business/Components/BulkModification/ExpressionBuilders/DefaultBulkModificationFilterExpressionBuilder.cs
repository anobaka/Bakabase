using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bootstrap.Extensions;
using Newtonsoft.Json;
using NPOI.SS.Formula.Functions;

namespace Bakabase.InsideWorld.Business.Components.BulkModification.ExpressionBuilders
{
    public class DefaultBulkModificationFilterExpressionBuilder : IBulkModificationFilterExpressionBuilder
    {
        public Expression<Func<ResourceDto, bool>> Build(BulkModificationFilter filter)
        {
            var targetType =
                SpecificTypeUtils<BulkModificationFilterProperty>.Type.GetField(filter.Property.ToString())!
                    .GetCustomAttributes<BulkModificationFilterPropertyOperationAttribute>(false)
                    .FirstOrDefault(a => a.Operation == filter.Operation)!.TargetType;
            var targetValue = string.IsNullOrEmpty(filter.Target) || targetType == null
                ? null
                : JsonConvert.DeserializeObject(filter.Target, targetType);
            var targetValueExp = Expression.Constant(targetValue);
            var propertyName = filter.Property.GetPropertyName();
            var parameter = Expression.Parameter(SpecificTypeUtils<ResourceDto>.Type);

            var property = Expression.Property(parameter, propertyName);
            var body = filter.Operation switch
            {
                BulkModificationFilterOperation.Equals => BuildEquals(property, targetValueExp),
                BulkModificationFilterOperation.NotEquals => BuildNotEquals(property, targetValueExp),
                BulkModificationFilterOperation.Contains => BuildContains(property, targetValueExp),
                BulkModificationFilterOperation.NotContains => BuildNotContains(property, targetValueExp),
                BulkModificationFilterOperation.StartsWith => BuildStartsWith(property, targetValueExp),
                BulkModificationFilterOperation.EndsWith => BuildEndsWith(property, targetValueExp),
                BulkModificationFilterOperation.GreaterThan => BuildGreaterThan(property, targetValueExp),
                BulkModificationFilterOperation.LessThan => BuildEquals(property, targetValueExp),
                BulkModificationFilterOperation.GreaterThanOrEquals => BuildGreaterThanOrEquals(property,
                    targetValueExp),
                BulkModificationFilterOperation.LessThanOrEquals => BuildLessThanOrEquals(property, targetValueExp),
                BulkModificationFilterOperation.IsNull => BuildIsNull(property),
                BulkModificationFilterOperation.IsNotNull => BuildIsNotNull(property),
                BulkModificationFilterOperation.In => BuildIn(property, targetValueExp),
                BulkModificationFilterOperation.NotIn => BuildNotIn(property, targetValueExp),
                _ => throw new ArgumentOutOfRangeException()
            };

            return Expression.Lambda<Func<ResourceDto, bool>>(body, parameter);
        }

        protected virtual Expression BuildEquals(Expression property, Expression targetValue) =>
            Expression.Equal(property, targetValue);

        protected virtual Expression BuildNotEquals(Expression property, Expression targetValue) =>
            Expression.NotEqual(property, targetValue);

        protected virtual Expression BuildContains(Expression property, Expression targetValue) =>
            Expression.Call(property, nameof(string.Contains), null, targetValue);

        protected virtual Expression BuildNotContains(Expression property, Expression targetValue) =>
            Expression.Not(BuildContains(property, targetValue));

        protected virtual Expression BuildStartsWith(Expression property, Expression targetValue) =>
            Expression.Call(property, nameof(string.StartsWith), null, targetValue);

        protected virtual Expression BuildEndsWith(Expression property, Expression targetValue) =>
            Expression.Call(property, nameof(string.EndsWith), null, targetValue);

        protected virtual Expression BuildGreaterThan(Expression property, Expression targetValue) =>
            Expression.GreaterThan(property, targetValue);

        protected virtual Expression BuildGreaterThanOrEquals(Expression property, Expression targetValue) =>
            Expression.GreaterThanOrEqual(property, targetValue);

        protected virtual Expression BuildLessThan(Expression property, Expression targetValue) =>
            Expression.LessThan(property, targetValue);

        protected virtual Expression BuildLessThanOrEquals(Expression property, Expression targetValue) =>
            Expression.LessThanOrEqual(property, targetValue);

        protected virtual Expression BuildIsNull(Expression property) =>
            Expression.Equal(property, Expression.Constant(null));

        protected virtual Expression BuildIsNotNull(Expression property) =>
            Expression.NotEqual(property, Expression.Constant(null));

        protected virtual Expression BuildIn(Expression property, Expression targetValue) =>
            Expression.Call(targetValue, "Contains", null, property);

        protected virtual Expression BuildNotIn(Expression property, Expression targetValue) =>
            Expression.Not(BuildIn(property, targetValue));

    }
}