﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models.Dtos;
using Bakabase.InsideWorld.Business.Components.BulkModification.DiffHandlers;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Aos;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bootstrap.Extensions;
using Newtonsoft.Json;

namespace Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions
{
    public static class BulkModificationExtensions
    {
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

        public static BulkModificationDto ToDto(this Models.BulkModification bm, BulkModificationTempData? tempData)
        {
            return new BulkModificationDto
            {
                CreatedAt = bm.CreatedAt,
                Id = bm.Id,
                Name = bm.Name,
                Status = bm.Status,
                CalculatedAt = bm.CalculatedAt,
                FilteredAt = bm.FilteredAt,
                AppliedAt = bm.AppliedAt,
                RevertedAt = bm.RevertedAt,


                Filter = string.IsNullOrEmpty(bm.Filter)
                    ? null
                    : JsonConvert.DeserializeObject<BulkModificationFilterGroup>(bm.Filter),
                Processes = string.IsNullOrEmpty(bm.Processes)
                    ? null
                    : JsonConvert.DeserializeObject<List<BulkModificationProcess>>(bm.Processes)!,
                Variables = string.IsNullOrEmpty(bm.Variables)
                    ? null
                    : JsonConvert.DeserializeObject<List<BulkModificationVariable>>(bm.Variables)!,

                FilteredResourceIds = tempData?.GetResourceIds()
            };
        }

        public static BulkModificationProperty ToBulkModificationProperty(this ResourceDiffProperty rdp)
        {
            return rdp switch
            {
                ResourceDiffProperty.Category => BulkModificationProperty.Category,
                ResourceDiffProperty.MediaLibrary => BulkModificationProperty.MediaLibrary,
                ResourceDiffProperty.ReleaseDt => BulkModificationProperty.ReleaseDt,
                ResourceDiffProperty.Publisher => BulkModificationProperty.Publisher,
                ResourceDiffProperty.Name => BulkModificationProperty.Name,
                ResourceDiffProperty.Language => BulkModificationProperty.Language,
                ResourceDiffProperty.Volume => BulkModificationProperty.Volume,
                ResourceDiffProperty.Original => BulkModificationProperty.Original,
                ResourceDiffProperty.Series => BulkModificationProperty.Series,
                ResourceDiffProperty.Tag => BulkModificationProperty.Tag,
                ResourceDiffProperty.Introduction => BulkModificationProperty.Introduction,
                ResourceDiffProperty.Rate => BulkModificationProperty.Rate,
                ResourceDiffProperty.CustomProperty => BulkModificationProperty.CustomProperty,
                _ => throw new ArgumentOutOfRangeException(nameof(rdp), rdp, null)
            };
        }

        public static BulkModificationDiffType ToBulkModificationDiffType(this ResourceDiffType rdt)
        {
            return rdt switch
            {
                ResourceDiffType.Added => BulkModificationDiffType.Added,
                ResourceDiffType.Removed => BulkModificationDiffType.Removed,
                ResourceDiffType.Modified => BulkModificationDiffType.Modified,
                _ => throw new ArgumentOutOfRangeException(nameof(rdt), rdt, null)
            };
        }

        public static BulkModificationDiff ToBulkModificationDiff(this ResourceDiff rd, int bmId, int resourceId,
            string resourcePath)
        {
            return new BulkModificationDiff
            {
                CurrentValue = rd.CurrentValue == null ? null : JsonConvert.SerializeObject(rd.CurrentValue),
                NewValue = rd.NewValue == null ? null : JsonConvert.SerializeObject(rd.NewValue),
                Operation = BulkModificationDiffOperation.None,
                Property = rd.Property.ToBulkModificationProperty(),
                PropertyKey = rd.Key,
                Type = rd.Type.ToBulkModificationDiffType(),
                BulkModificationId = bmId,
                ResourceId = resourceId,
                ResourcePath = resourcePath
            };
        }

        public static ConcurrentDictionary<BulkModificationProperty, IBulkModificationDiffHandler> DiffHandlers = new()
        {
            [BulkModificationProperty.Category] = new BmCategoryDiffHandler(),
            [BulkModificationProperty.MediaLibrary] = new BmMediaLibraryDiffHandler(),
            [BulkModificationProperty.ReleaseDt] = new BmReleaseDtDiffHandler(),
            [BulkModificationProperty.Publisher] = new BmPublisherDiffHandler(),
            [BulkModificationProperty.Name] = new BmNameDiffHandler(),
            [BulkModificationProperty.Language] = new BmLanguageDiffHandler(),
            [BulkModificationProperty.Volume] = new BmVolumeDiffHandler(),
            [BulkModificationProperty.Original] = new BmOriginalDiffHandler(),
            [BulkModificationProperty.Series] = new BmSeriesDiffHandler(),
            [BulkModificationProperty.Tag] = new BmTagDiffHandler(),
            [BulkModificationProperty.Introduction] = new BmIntroductionDiffHandler(),
            [BulkModificationProperty.Rate] = new BmRateDiffHandler(),
            [BulkModificationProperty.CustomProperty] = new BmCustomPropertyDiffHandler()
        };

        public static void Apply(this BulkModificationDiff diff, ResourceDto resource)
        {
            DiffHandlers.GetValueOrDefault(diff.Property)?.Apply(resource, diff);
        }

        public static void Revert(this BulkModificationDiff diff, ResourceDto resource)
        {
            DiffHandlers.GetValueOrDefault(diff.Property)?.Revert(resource, diff);
        }

        public static Models.BulkModification Duplicate(this Models.BulkModification bm)
        {
            return bm with
            {
                Id = 0,
                FilteredAt = null,
                CreatedAt = DateTime.Now,
                AppliedAt = null,
                CalculatedAt = null,
                RevertedAt = null,
                Status = BulkModificationStatus.Processing
            };
        }
    }
}