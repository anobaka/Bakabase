using System;
using System.Collections.Concurrent;
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
using Bakabase.Modules.StandardValue.Extensions;
using Bakabase.InsideWorld.Business.Models.Db;
using ResourceDiff = Bakabase.Abstractions.Models.Domain.ResourceDiff;

namespace Bakabase.InsideWorld.Business.Extensions
{
    public static class ResourceExtensions
    {
        public static Resource? ToDomainModel(this Abstractions.Models.Db.ResourceDbModel? r)
        {
            if (r == null)
            {
                return null;
            }

            return new()
            {
                Id = r.Id,
                CategoryId = r.CategoryId,
                MediaLibraryId = r.MediaLibraryId,
                CreatedAt = r.CreateDt,
                UpdatedAt = r.UpdateDt,
                FileCreatedAt = r.FileCreateDt,
                FileModifiedAt = r.FileModifyDt,
                IsFile = r.IsFile,
                HasChildren = r.HasChildren,
                ParentId = r.ParentId,
                Directory = Path.GetDirectoryName(r.Path)!.StandardizePath()!,
                FileName = Path.GetFileName(r.Path),
                Pinned = r.Pinned,
            };
        }

        public static Abstractions.Models.Db.ResourceDbModel? ToDbModel(this Resource? r)
        {
            if (r == null)
            {
                return null;
            }

            return new()
            {
                Id = r.Id,
                CreateDt = r.CreatedAt,
                CategoryId = r.CategoryId,
                MediaLibraryId = r.MediaLibraryId,
                UpdateDt = r.UpdatedAt,
                FileCreateDt = r.FileCreatedAt,
                FileModifyDt = r.FileModifiedAt,
                HasChildren = r.HasChildren,
                ParentId = r.Parent?.Id ?? r.ParentId,
                IsFile = r.IsFile,
                Path = r.Path,
                Pinned = r.Pinned,
            };
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
                                    if (cv.BizValue != v.BizValue)
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
                current.Tags.Add(ResourceTag.IsParent);
            }
            else
            {
                if (string.IsNullOrEmpty(current.Parent?.Path) &&
                    string.IsNullOrEmpty(patches.Parent?.Path))
                {
                    if (current.ParentId.HasValue)
                    {
                        current.ParentId = null;
                        current.Tags.RemoveWhere(x => x == ResourceTag.IsParent);
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

            return changed;
        }
    }
}