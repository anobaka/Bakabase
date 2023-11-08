using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bootstrap.Extensions;
using Newtonsoft.Json;

namespace Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions
{
    public static class BulkModificationExtensions
    {
        public static string GetPropertyName(this BulkModificationProperty property)
        {
            return property switch
            {
                BulkModificationProperty.Category => nameof(ResourceDto.CategoryId),
                BulkModificationProperty.MediaLibrary => nameof(ResourceDto.MediaLibraryId),
                BulkModificationProperty.Name => nameof(ResourceDto.Name),
                BulkModificationProperty.RawName => nameof(ResourceDto.RawName),
                BulkModificationProperty.RawFullname => nameof(ResourceDto.RawFullname),
                BulkModificationProperty.ReleaseDt => nameof(ResourceDto.ReleaseDt),
                BulkModificationProperty.CreateDt => nameof(ResourceDto.CreateDt),
                BulkModificationProperty.FileCreateDt => nameof(ResourceDto.FileCreateDt),
                BulkModificationProperty.FileModifyDt => nameof(ResourceDto.FileModifyDt),
                BulkModificationProperty.Publisher => nameof(ResourceDto.Publishers),
                BulkModificationProperty.Language => nameof(ResourceDto.Language),
                BulkModificationProperty.Volume => nameof(ResourceDto.Volume),
                BulkModificationProperty.Original => nameof(ResourceDto.Originals),
                BulkModificationProperty.Series => nameof(ResourceDto.Series),
                BulkModificationProperty.Tag => nameof(ResourceDto.Tags),
                BulkModificationProperty.Introduction => nameof(ResourceDto.Introduction),
                BulkModificationProperty.Rate => nameof(ResourceDto.Rate),
                BulkModificationProperty.CustomProperty => nameof(ResourceDto.CustomProperties),
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

        public static BulkModificationDto ToDto(this Models.BulkModification bm)
        {
            return new BulkModificationDto
            {
                CreatedAt = bm.CreatedAt,
                Id = bm.Id,
                Name = bm.Name,
                Status = bm.Status,

                Filter = string.IsNullOrEmpty(bm.Filter)
                    ? null
                    : JsonConvert.DeserializeObject<BulkModificationFilterGroup>(bm.Filter),
            };
        }

        public static Dictionary<ResourceDto, List<BulkModificationDiff>> Process(this BulkModificationProcess process,
            List<ResourceDto> resources)
        {
            var diffs = new Dictionary<ResourceDto, List<BulkModificationDiff>>();

            foreach (var r in resources)
            {
                switch (process.Operation)
                {
                    case BulkModificationProcessOperation.Standardize:
                        break;
                    case BulkModificationProcessOperation.Add:
                        break;
                    case BulkModificationProcessOperation.Replace:
                        break;
                    case BulkModificationProcessOperation.Remove:
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }
    }
}