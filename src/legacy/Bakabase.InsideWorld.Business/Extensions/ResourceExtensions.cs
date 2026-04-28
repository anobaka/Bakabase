using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.StandardValue;

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
                MediaLibraryId = dbModel.MediaLibraryId,
                CreatedAt = dbModel.CreateDt,
                UpdatedAt = dbModel.UpdateDt,
                FileCreatedAt = dbModel.FileCreateDt.TruncateToMilliseconds(),
                FileModifiedAt = dbModel.FileModifyDt.TruncateToMilliseconds(),
                IsFile = dbModel.IsFile,
                ParentId = dbModel.ParentId,
                // 直接设置 Path，避免 Directory/FileName 分别设置触发两次 StandardizePath
                Path = dbModel.Path,
                Tags = TagsCache.GetValueOrDefault(validTagBits, EmptyTags),
                PlayedAt = dbModel.PlayedAt,
                Status = dbModel.Status,
            };
            return domainModel;
        }

        public static Abstractions.Models.Db.ResourceDbModel ToDbModel(this Resource domainModel)
        {
            var dbModel = new Abstractions.Models.Db.ResourceDbModel
            {
                Id = domainModel.Id,
                CreateDt = domainModel.CreatedAt,
                MediaLibraryId = domainModel.MediaLibraryId,
                UpdateDt = domainModel.UpdatedAt,
                FileCreateDt = domainModel.FileCreatedAt.TruncateToMilliseconds(),
                FileModifyDt = domainModel.FileModifiedAt.TruncateToMilliseconds(),
                ParentId = domainModel.Parent?.Id ?? domainModel.ParentId,
                IsFile = domainModel.IsFile,
                Path = domainModel.Path,
                Tags = domainModel.Tags.Aggregate(default(ResourceTag), (s, t) => (s | t)),
                PlayedAt = domainModel.PlayedAt,
                Status = domainModel.Status,
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

            var patchFileCreated = patches.FileCreatedAt.TruncateToMilliseconds();
            var patchFileModified = patches.FileModifiedAt.TruncateToMilliseconds();
            if (patchFileCreated != default &&
                current.FileCreatedAt.TruncateToMilliseconds().Ticks != patchFileCreated.Ticks)
            {
                current.FileCreatedAt = patchFileCreated;
                changed = true;
            }

            if (patchFileModified != default &&
                current.FileModifiedAt.TruncateToMilliseconds().Ticks != patchFileModified.Ticks)
            {
                current.FileModifiedAt = patchFileModified;
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