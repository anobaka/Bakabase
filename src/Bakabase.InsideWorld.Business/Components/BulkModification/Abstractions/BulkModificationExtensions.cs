using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bootstrap.Extensions;

namespace Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions
{
    public static class BulkModificationExtensions
    {
        public static string GetPropertyName(this BulkModificationFilterProperty property)
        {
            return property switch
            {
                BulkModificationFilterProperty.Category => nameof(ResourceDto.CategoryId),
                BulkModificationFilterProperty.MediaLibrary => nameof(ResourceDto.MediaLibraryId),
                BulkModificationFilterProperty.Name => nameof(ResourceDto.Name),
                BulkModificationFilterProperty.RawName => nameof(ResourceDto.RawName),
                BulkModificationFilterProperty.RawFullname => nameof(ResourceDto.RawFullname),
                BulkModificationFilterProperty.ReleaseDt => nameof(ResourceDto.ReleaseDt),
                BulkModificationFilterProperty.CreateDt => nameof(ResourceDto.CreateDt),
                BulkModificationFilterProperty.FileCreateDt => nameof(ResourceDto.FileCreateDt),
                BulkModificationFilterProperty.FileModifyDt => nameof(ResourceDto.FileModifyDt),
                BulkModificationFilterProperty.Publisher => nameof(ResourceDto.Publishers),
                BulkModificationFilterProperty.Language => nameof(ResourceDto.Language),
                BulkModificationFilterProperty.Volume => nameof(ResourceDto.Volume),
                BulkModificationFilterProperty.Original => nameof(ResourceDto.Originals),
                BulkModificationFilterProperty.Series => nameof(ResourceDto.Series),
                BulkModificationFilterProperty.Tag => nameof(ResourceDto.Tags),
                BulkModificationFilterProperty.Introduction => nameof(ResourceDto.Introduction),
                BulkModificationFilterProperty.Rate => nameof(ResourceDto.Rate),
                BulkModificationFilterProperty.CustomProperty => nameof(ResourceDto.CustomProperties),
                _ => throw new ArgumentOutOfRangeException(nameof(property), property, null)
            };
        }

        public static Expression<Func<ResourceDto, bool>> BuildExpression(this BulkModificationFilter filter)
        {
            var builder = BulkModificationFilterExpressionBuilders.Builders[filter.Property];
            return builder.Build(filter);
        }

        public static Expression<Func<ResourceDto, bool>> BuildExpression(this BulkModificationFilterGroup group)
        {
            var exps = new List<Expression<Func<ResourceDto, bool>>>();
            if (group.Filters != null)
            {
                exps.AddRange(group.Filters.Select(f => f.BuildExpression()));
            }

            if (group.Groups != null)
            {
                exps.AddRange(group.Groups.Select(g => g.BuildExpression()));
            }

            var exp = exps.First();
            var restExps = exps.Skip(1);
            return group.Operation switch
            {
                BulkModificationFilterGroupOperation.And => restExps.Aggregate(exp, (s, t) => s.And(t)),
                BulkModificationFilterGroupOperation.Or => restExps.Aggregate(exp, (s, t) => s.Or(t)),
                _ => throw new ArgumentOutOfRangeException()
            };
        }
    }
}