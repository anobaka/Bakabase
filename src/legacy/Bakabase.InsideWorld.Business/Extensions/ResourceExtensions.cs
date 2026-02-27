using System;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Collections.Generic;
using Bakabase.InsideWorld.Business.Models.Domain;
using Bakabase.InsideWorld.Models.Extensions;
using Bakabase.InsideWorld.Models.Models.Dtos;
using System.Linq;
using Bakabase.Abstractions.Models.Input;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Constants.Aos;
using Bakabase.InsideWorld.Models.Models.Aos;
using Bootstrap.Extensions;
using CsQuery.ExtensionMethods.Internal;
using Bakabase.InsideWorld.Models.Models.Entities;
using System.IO;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Components;
using Bakabase.Modules.StandardValue;
using Bakabase.Modules.StandardValue.Extensions;
using Bakabase.InsideWorld.Business.Models.Db;
using Bakabase.Modules.StandardValue.Abstractions.Configurations;
using ResourceDiff = Bakabase.Abstractions.Models.Domain.ResourceDiff;

namespace Bakabase.InsideWorld.Business.Extensions
{
    public static class ResourceExtensions
    {
        // 只缓存有效的 ResourceTag 组合（IsParent=1, Pinned=2），共 4 种可能
        private static readonly ResourceTag[] ValidTags = [ResourceTag.IsParent, ResourceTag.Pinned];
        private static readonly FrozenDictionary<ResourceTag, FrozenSet<ResourceTag>> TagsCache =
            Enumerable.Range(0, 4).ToFrozenDictionary(
                i => (ResourceTag)i,
                i => ValidTags.Where(t => ((ResourceTag)i & t) == t).ToFrozenSet());
        private static readonly FrozenSet<ResourceTag> EmptyTags = FrozenSet<ResourceTag>.Empty;

        public static Resource ToDomainModel(this Abstractions.Models.Db.ResourceDbModel dbModel)
        {
            // 只保留有效的 tag 位（IsParent | Pinned = 3）
            var validTagBits = dbModel.Tags & (ResourceTag.IsParent | ResourceTag.Pinned);
            var domainModel = new Resource()
            {
                Id = dbModel.Id,
                CategoryId = dbModel.CategoryId,
                MediaLibraryId = dbModel.MediaLibraryId,
                CreatedAt = dbModel.CreateDt,
                UpdatedAt = dbModel.UpdateDt,
                FileCreatedAt = dbModel.FileCreateDt,
                FileModifiedAt = dbModel.FileModifyDt,
                IsFile = dbModel.IsFile,
                ParentId = dbModel.ParentId,
                // 直接设置 Path，避免 Directory/FileName 分别设置触发两次 StandardizePath
                Path = dbModel.Path,
                Tags = TagsCache.GetValueOrDefault(validTagBits, EmptyTags),
                PlayedAt = dbModel.PlayedAt,
                Source = dbModel.Source,
                Status = dbModel.Status,
                SourceKey = dbModel.SourceKey
            };
            return domainModel;
        }

        public static Abstractions.Models.Db.ResourceDbModel ToDbModel(this Resource domainModel)
        {
            var dbModel = new Abstractions.Models.Db.ResourceDbModel
            {
                Id = domainModel.Id,
                CreateDt = domainModel.CreatedAt,
                CategoryId = domainModel.CategoryId,
                MediaLibraryId = domainModel.MediaLibraryId,
                UpdateDt = domainModel.UpdatedAt,
                FileCreateDt = domainModel.FileCreatedAt,
                FileModifyDt = domainModel.FileModifiedAt,
                ParentId = domainModel.Parent?.Id ?? domainModel.ParentId,
                IsFile = domainModel.IsFile,
                Path = domainModel.Path,
                Tags = domainModel.Tags.Aggregate(default(ResourceTag), (s, t) => (s | t)),
                PlayedAt = domainModel.PlayedAt,
                Source = domainModel.Source,
                Status = domainModel.Status,
                SourceKey = domainModel.SourceKey
            };
            return dbModel;
        }


        // public static bool EnoughToGenerateNfo(this Resource? resource)
        // {
        //     if (resource == null)
        //     {
        //         return false;
        //     }
        //
        //     if (resource.Tags?.Any() == true
        //         || resource.CustomPropertyValues?.Any() == true
        //         || resource.ParentId.HasValue)
        //     {
        //         return true;
        //     }
        //
        //     return false;
        // }

        public static bool MergeOnSynchronization(this Resource current, Resource patches)
        {
            var changed = false;

            if (patches.IsFile != current.IsFile)
            {
                current.IsFile = patches.IsFile;
                changed = true;
            }

            if (current.FileCreatedAt != patches.FileCreatedAt ||
                current.FileModifiedAt != patches.FileModifiedAt)
            {
                current.FileCreatedAt = patches.FileCreatedAt;
                changed = true;
            }

            if (current.FileModifiedAt != patches.FileModifiedAt)
            {
                current.FileModifiedAt = patches.FileModifiedAt;
                changed = true;
            }

            if (patches.Properties?.Any() == true)
            {
                foreach (var (pt, pm) in patches.Properties)
                {
                    current.Properties ??= [];
                    if (!current.Properties.TryGetValue(pt, out var cpm))
                    {
                        current.Properties[pt] = pm;
                        changed = true;
                        continue;
                    }

                    foreach (var (pId, p) in pm)
                    {
                        if (!cpm.TryGetValue(pId, out var cp))
                        {
                            cpm[pId] = p;
                            changed = true;
                            continue;
                        }

                        if (p.Values != null)
                        {
                            foreach (var v in p.Values)
                            {
                                var cv = cp.Values?.FirstOrDefault(x => x.Scope == v.Scope);
                                if (cv == null)
                                {
                                    (cp.Values ??= []).Add(v);
                                    changed = true;
                                }
                                else
                                {
                                    if (!StandardValueSystem.GetHandler(p.BizValueType)
                                            .Compare(cv.BizValue, v.BizValue))
                                    {
                                        cv.BizValue = v.BizValue;
                                        changed = true;
                                    }
                                }
                            }
                        }
                    }
                }
            }

            if (current.Parent?.Path != patches.Parent?.Path)
            {
                current.Parent = patches.Parent;
                changed = true;
            }
            else
            {
                if (string.IsNullOrEmpty(current.Parent?.Path) &&
                    string.IsNullOrEmpty(patches.Parent?.Path))
                {
                    if (current.ParentId.HasValue)
                    {
                        current.ParentId = null;
                        changed = true;
                    }
                }
            }

            if (current.MediaLibraryId != patches.MediaLibraryId && patches.MediaLibraryId > 0)
            {
                current.MediaLibraryId = patches.MediaLibraryId;
                changed = true;
            }

            if (current.CategoryId != patches.CategoryId && patches.CategoryId > 0)
            {
                current.CategoryId = patches.CategoryId;
                changed = true;
            }

            if (patches.PlayedAt.HasValue && current.PlayedAt != patches.PlayedAt)
            {
                current.PlayedAt = patches.PlayedAt;
                changed = true;
            }

            if (current.Path != patches.Path)
            {
                current.Path = patches.Path;
                changed = true;
            }

            return changed;
        }
    }
}