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
using Bakabase.InsideWorld.Models.Components;

namespace Bakabase.InsideWorld.Business.Extensions
{
    public static class ResourceExtensions
    {
        public static Resource? ToDomainModel(this Abstractions.Models.Db.Resource? r)
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
            };
        }

        public static Abstractions.Models.Db.Resource? ToDbModel(this Resource? r)
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
                Path = r.Path
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

        // /// <summary>
        // /// Remove invalid and duplicate data
        // /// </summary>
        // /// <param name="resource"></param>
        // [Obsolete]
        // public static void Clean(this Resource resource)
        // {
        //     if (resource.Publishers?.Any() == true)
        //     {
        //         resource.Publishers.RemoveInvalid();
        //     }
        //
        //     if (resource.Originals?.Any() == true)
        //     {
        //         resource.Originals.RemoveAll(a => ExtensionMethods.IsNullOrEmpty(a.Name) && a.Id == 0);
        //     }
        //
        //     if (resource.Series != null)
        //     {
        //         if (resource.Series.Id == 0 && ExtensionMethods.IsNullOrEmpty(resource.Series.Name))
        //         {
        //             resource.Series = null;
        //         }
        //     }
        //
        //     if (resource.Tags?.Any() == true)
        //     {
        //         resource.Tags.RemoveAll(a => ExtensionMethods.IsNullOrEmpty(a.Name) && a.Id == 0);
        //     }
        // }

        // [Obsolete]
        // public static List<ResourceDiff> Compare(this Resource a, Resource b)
        // {
        //     var diffs = new List<ResourceDiff?>();
        //     foreach (var property in SpecificEnumUtils<ResourceDiffProperty>.Values)
        //     {
        //         var getter = property.GetGetter();
        //         var va = getter(a);
        //         var vb = getter(b);
        //         diffs.Add(property.Compare(va, vb));
        //     }
        //
        //     return diffs.Where(d => d != null).ToList()!;
        // }
        //
        // [Obsolete]
        // public static ConcurrentDictionary<ResourceDiffProperty, Func<Resource, object?>> ResourcePropertyGetters =
        //     new(new Dictionary<ResourceDiffProperty, Func<Resource, object?>>
        //     {
        //         {ResourceDiffProperty.Category, r => r.CategoryId},
        //         {ResourceDiffProperty.MediaLibrary, r => r.MediaLibraryId},
        //         {ResourceDiffProperty.ReleaseDt, r => r.ReleaseDt},
        //         {ResourceDiffProperty.Publisher, r => r.Publishers},
        //         {ResourceDiffProperty.Name, r => r.Name},
        //         {ResourceDiffProperty.Language, r => r.Language},
        //         {ResourceDiffProperty.Volume, r => r.Volume},
        //         {ResourceDiffProperty.Original, r => r.Originals},
        //         {ResourceDiffProperty.Series, r => r.Series},
        //         {ResourceDiffProperty.Tag, r => r.Tags},
        //         {ResourceDiffProperty.Introduction, r => r.Introduction},
        //         {ResourceDiffProperty.Rate, r => r.Rate},
        //         {ResourceDiffProperty.CustomProperty, r => r.CustomProperties}
        //     });
        //
        // [Obsolete]
        // public static Func<Resource, object?> GetGetter(this ResourceDiffProperty property) =>
        //     ResourcePropertyGetters.TryGetValue(property, out var getter)
        //         ? getter
        //         : throw new InvalidOperationException($"Can\'t get getter of property [{(int)property}:{property}]");

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
                            foreach(var v in p.Values)
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

            return changed;
        }

        // [Obsolete]
        // public static string GetPropertyName(this ResourceDiffProperty property)
        // {
        //     return property switch
        //     {
        //         ResourceDiffProperty.ReleaseDt => nameof(Resource.ReleaseDt),
        //         ResourceDiffProperty.Name => nameof(Resource.Name),
        //         ResourceDiffProperty.Language => nameof(Resource.Language),
        //         ResourceDiffProperty.Introduction => nameof(Resource.Introduction),
        //         ResourceDiffProperty.Rate => nameof(Resource.Rate),
        //         ResourceDiffProperty.Volume => nameof(Resource.Volume),
        //         ResourceDiffProperty.Original => nameof(Resource.Originals),
        //         ResourceDiffProperty.Series => nameof(Resource.Series),
        //         ResourceDiffProperty.Publisher => nameof(Resource.Publishers),
        //         ResourceDiffProperty.Tag => nameof(Resource.Tags),
        //         ResourceDiffProperty.CustomProperty => nameof(Resource.CustomProperties),
        //         ResourceDiffProperty.Category => nameof(Resource.CategoryId),
        //         ResourceDiffProperty.MediaLibrary => nameof(Resource.MediaLibraryId),
        //         _ => throw new ArgumentOutOfRangeException(nameof(property), property, null)
        //     };
        // }

        // [Obsolete]
        // public static ResourceDiff? Compare(this ResourceDiffProperty property, object? a, object? b)
        // {
        //     switch (property)
        //     {
        //         case ResourceDiffProperty.ReleaseDt:
        //         case ResourceDiffProperty.Name:
        //         case ResourceDiffProperty.Language:
        //         case ResourceDiffProperty.Introduction:
        //         case ResourceDiffProperty.Rate:
        //             {
        //                 return ResourceDiff.BuildRootDiff(property, a, b, EqualityComparer<object>.Default,
        //                     property.GetPropertyName(), null);
        //             }
        //         case ResourceDiffProperty.Volume:
        //             {
        //                 return ResourceDiff.BuildRootDiff(property, a as VolumeDto, b as VolumeDto, VolumeDto.BizComparer,
        //                     property.GetPropertyName(), VolumeExtensions.Compare);
        //             }
        //         case ResourceDiffProperty.Series:
        //             {
        //                 return ResourceDiff.BuildRootDiff(property, a as SeriesDto, b as SeriesDto, SeriesDto.BizComparer,
        //                     property.GetPropertyName(), SeriesExtensions.Compare);
        //             }
        //         case ResourceDiffProperty.Publisher:
        //             {
        //                 return ResourceDiff.BuildRootDiffForArrayProperty(property, a as List<PublisherDto>,
        //                     b as List<PublisherDto>,
        //                     PublisherDto.BizComparer,
        //                     property.GetPropertyName(), PublisherExtensions.Compare);
        //             }
        //         case ResourceDiffProperty.Tag:
        //             {
        //                 return ResourceDiff.BuildRootDiffForArrayProperty(property, a as List<TagDto>, b as List<TagDto>,
        //                     TagDto.BizComparer, property.GetPropertyName(), TagExtensions.Compare);
        //             }
        //         case ResourceDiffProperty.Original:
        //             {
        //                 return ResourceDiff.BuildRootDiffForArrayProperty(property, a as List<OriginalDto>,
        //                     b as List<OriginalDto>,
        //                     OriginalDto.BizComparer, property.GetPropertyName(),
        //                     OriginalExtensions.Compare);
        //             }
        //         case ResourceDiffProperty.CustomProperty:
        //             return (a as List<CustomResourceProperty>).Compare(b as List<CustomResourceProperty>);
        //         case ResourceDiffProperty.Category:
        //         case ResourceDiffProperty.MediaLibrary:
        //             return ResourceDiff.BuildRootDiff(property, (int)a!, (int)b!, IntEqualityComparer.Default, null,
        //                 null);
        //         default:
        //             throw new ArgumentOutOfRangeException();
        //     }
        // }

        // [Obsolete]
        // public static ResourceDiff? Compare(this List<CustomResourceProperty>? a, List<CustomResourceProperty>? b)
        // {
        //     if (a == null && b == null)
        //     {
        //         return null;
        //     }
        //
        //     if (a == null)
        //     {
        //         return ResourceDiff.Added(ResourceDiffProperty.CustomProperty, b);
        //     }
        //
        //     if (b == null)
        //     {
        //         return ResourceDiff.Removed(ResourceDiffProperty.CustomProperty, a);
        //     }
        //
        //     var aMapByKey = a.GroupBy(x => x.Key).ToDictionary(x => x.Key, x => x.OrderBy(c => c.Index).ToList());
        //     var bMapByKey = b.GroupBy(x => x.Key).ToDictionary(x => x.Key, x => x.OrderBy(c => c.Index).ToList());
        //
        //     var allKeys = aMapByKey.Keys.Union(bMapByKey.Keys).ToHashSet();
        //
        //     var diffsByKey = new List<ResourceDiff>();
        //
        //     foreach (var key in allKeys)
        //     {
        //         var aValuesByKey = aMapByKey.GetValueOrDefault(key);
        //         var bValuesByKey = bMapByKey.GetValueOrDefault(key);
        //
        //         ResourceDiff? keyDiff = null;
        //
        //         // At least one of the values is not null
        //         if (aValuesByKey == null)
        //         {
        //             keyDiff = ResourceDiff.Added(ResourceDiffProperty.CustomProperty, bValuesByKey);
        //         }
        //         else
        //         {
        //             if (bValuesByKey == null)
        //             {
        //                 keyDiff = ResourceDiff.Removed(ResourceDiffProperty.CustomProperty, aValuesByKey);
        //             }
        //             else
        //             {
        //                 // check elements
        //                 var maxCount = Math.Max(aValuesByKey.Count, bValuesByKey.Count);
        //                 var diffsByIndex = new List<ResourceDiff>();
        //                 for (var i = 0; i < maxCount; i++)
        //                 {
        //                     var aIndexedValue = aValuesByKey.ElementAtOrDefault(i);
        //                     var bIndexedValue = bValuesByKey.ElementAtOrDefault(i);
        //
        //                     ResourceDiff? indexedValueDiff = null;
        //
        //                     // At least one of the values is not null
        //                     if (aIndexedValue == null)
        //                     {
        //                         indexedValueDiff =
        //                             ResourceDiff.Added(ResourceDiffProperty.CustomProperty, bIndexedValue);
        //                     }
        //                     else
        //                     {
        //                         if (bIndexedValue == null)
        //                         {
        //                             indexedValueDiff =
        //                                 ResourceDiff.Removed(ResourceDiffProperty.CustomProperty, aIndexedValue);
        //                         }
        //                         else
        //                         {
        //                             if (aIndexedValue.Value != bIndexedValue.Value)
        //                             {
        //                                 indexedValueDiff = new ResourceDiff
        //                                 {
        //                                     CurrentValue = aIndexedValue,
        //                                     NewValue = bIndexedValue,
        //                                     Key = key,
        //                                     Property = ResourceDiffProperty.CustomProperty,
        //                                     Type = ResourceDiffType.Modified,
        //                                     SubDiffs = new List<ResourceDiff>
        //                                     {
        //                                         new ResourceDiff
        //                                         {
        //                                             CurrentValue = aIndexedValue?.Value,
        //                                             NewValue = bIndexedValue?.Value,
        //                                             Type = ResourceDiffType.Modified,
        //                                             Property = ResourceDiffProperty.CustomProperty,
        //                                             Key = i.ToString()
        //                                         }
        //                                     }
        //                                 };
        //                             }
        //                         }
        //                     }
        //
        //
        //                     if (indexedValueDiff != null)
        //                     {
        //                         diffsByIndex.Add(indexedValueDiff);
        //                     }
        //                 }
        //
        //                 if (diffsByIndex.Any())
        //                 {
        //                     keyDiff ??= new ResourceDiff
        //                     {
        //                         Key = key,
        //                         Property = ResourceDiffProperty.CustomProperty,
        //                         Type = ResourceDiffType.Modified,
        //                         SubDiffs = diffsByIndex,
        //                         CurrentValue = aValuesByKey,
        //                         NewValue = bValuesByKey
        //                     };
        //                 }
        //             }
        //         }
        //
        //         if (keyDiff != null)
        //         {
        //             diffsByKey.Add(keyDiff);
        //         }
        //     }
        //
        //     return diffsByKey.Any()
        //         ? new ResourceDiff
        //         {
        //             CurrentValue = a,
        //             NewValue = b,
        //             Key = nameof(Resource.CustomProperties),
        //             Property = ResourceDiffProperty.CustomProperty,
        //             SubDiffs = diffsByKey,
        //             Type = ResourceDiffType.Modified
        //         }
        //         : null;
        // }

        // public static Resource Clone(this Resource r)
        // {
        //     return r with
        //     {
        //         Parent = r.Parent?.Clone(),
        //         CustomPropertiesV2 = r.CustomPropertiesV2?.Select(p => p with { }).ToList(),
        //         CustomPropertyValues = r.CustomPropertyValues?.Select(p => p == null ? null : p with { }).ToList(),
        //         Tags = r.Tags?.Select(t => t with { }).ToList()
        //     };
        // }

        // public static string BuildPath(this Abstractions.Models.Db.Resource r)
        // {
        //     return Path
        //         .Combine(new[] { r.Directory, r.RawName }.Where(a => !string.IsNullOrEmpty(a)).ToArray())
        //         .StandardizePath()!;
        // }
    }
}