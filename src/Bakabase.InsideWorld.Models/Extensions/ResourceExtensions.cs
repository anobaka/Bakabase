using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Policy;
using System.Threading;
using System.Threading.Channels;
using Bakabase.InsideWorld.Models.Components;
using Bakabase.InsideWorld.Models.Configs.Resource;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Aos;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bootstrap.Extensions;
using Humanizer;
using Humanizer.Localisation;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Newtonsoft.Json;
using SharpCompress.Common;

namespace Bakabase.InsideWorld.Models.Extensions
{
    public static class ResourceExtensions
    {
        #region Helpers

        public static ConcurrentDictionary<ResourceDiffProperty, Action<ResourceDto, object?>> ResourcePropertySetters =
            new(new Dictionary<ResourceDiffProperty, Action<ResourceDto, object?>>
            {
                {ResourceDiffProperty.ReleaseDt, (r, v) => r.ReleaseDt = (DateTime?) v},
                {
                    ResourceDiffProperty.Publisher,
                    (r, v) => r.Publishers = (List<PublisherDto>?) v ?? new List<PublisherDto>()
                },
                // {ResourceDiffProperty.Name, r => r.Name},
                // {ResourceDiffProperty.Language, r => r.Language},
                // {ResourceDiffProperty.Volume, r => r.Volume},
                // {ResourceDiffProperty.Original, r => r.Originals},
                // {ResourceDiffProperty.Series, r => r.Series},
                // {ResourceDiffProperty.Tag, r => r.Tags},
                // {ResourceDiffProperty.Introduction, r => r.Introduction},
                // {ResourceDiffProperty.Rate, r => r.Rate},
                // {ResourceDiffProperty.CustomProperty, r => r.CustomProperties}
            });

        public static Action<ResourceDto, object?> GetSetter(this ResourceDiffProperty property) =>
            ResourcePropertySetters.TryGetValue(property, out var setter)
                ? setter
                : throw new InvalidOperationException($"Can\'t get setter of property [{(int) property}:{property}]");

        public static ConcurrentDictionary<ResourceDiffProperty, Func<ResourceDto, object?>> ResourcePropertyGetters =
            new(new Dictionary<ResourceDiffProperty, Func<ResourceDto, object?>>
            {
                {ResourceDiffProperty.Category, r => r.CategoryId},
                {ResourceDiffProperty.MediaLibrary, r => r.MediaLibraryId},
                {ResourceDiffProperty.ReleaseDt, r => r.ReleaseDt},
                {ResourceDiffProperty.Publisher, r => r.Publishers},
                {ResourceDiffProperty.Name, r => r.Name},
                {ResourceDiffProperty.Language, r => r.Language},
                {ResourceDiffProperty.Volume, r => r.Volume},
                {ResourceDiffProperty.Original, r => r.Originals},
                {ResourceDiffProperty.Series, r => r.Series},
                {ResourceDiffProperty.Tag, r => r.Tags},
                {ResourceDiffProperty.Introduction, r => r.Introduction},
                {ResourceDiffProperty.Rate, r => r.Rate},
                {ResourceDiffProperty.CustomProperty, r => r.CustomProperties}
            });

        public static Func<ResourceDto, object?> GetGetter(this ResourceDiffProperty property) =>
            ResourcePropertyGetters.TryGetValue(property, out var getter)
                ? getter
                : throw new InvalidOperationException($"Can\'t get getter of property [{(int) property}:{property}]");

        public static bool IsListProperty(this ResourceDiffProperty property)
        {
            return property switch
            {
                ResourceDiffProperty.Publisher => true,
                ResourceDiffProperty.Tag => true,
                ResourceDiffProperty.Original => true,
                ResourceDiffProperty.CustomProperty => true,
                ResourceDiffProperty.Name => false,
                ResourceDiffProperty.Language => false,
                ResourceDiffProperty.Volume => false,
                ResourceDiffProperty.Series => false,
                ResourceDiffProperty.Introduction => false,
                ResourceDiffProperty.Category => false,
                ResourceDiffProperty.MediaLibrary => false,
                ResourceDiffProperty.ReleaseDt => false,
                ResourceDiffProperty.Rate => false,
                _ => false
            };
        }

        #endregion

        #region Compatible

        #endregion

        #region NFO

        public static bool EnoughToGenerateNfo(this ResourceDto resource)
        {
            if (resource == null)
            {
                return false;
            }

            if (resource.Rate > 0 ||
                resource.Tags?.Any() == true ||
                resource.Name.IsNotEmpty() ||
                resource.Publishers?.Any() == true ||
                resource.Series != null ||
                resource.Introduction.IsNotEmpty() ||
                resource.Originals?.Any() == true
               )
            {
                return true;
            }

            return false;
        }



        #endregion

        #region Analyze

        public static string BuildGroupKey(this ResourceDto sr)
        {
            var n = sr.Publishers.BuildPublisherString() + sr.Name;
            if (sr.Volume != null)
            {
                n += $" {sr.Volume.BuildVolumeString()}";
            }

            return n;
        }

        /// <summary>
        /// 优先级：Publisher（完整性）>Language>Original>Serial
        /// </summary>
        /// <param name="resource"></param>
        /// <returns></returns>
        public static int GetInformationScore(this ResourceDto resource)
        {
            var score = 0;
            if (resource.Publishers.Any())
            {
                score += 100;
            }

            if (resource.Language == ResourceLanguage.Chinese)
            {
                score += 10;
            }

            if (resource.ReleaseDt.HasValue)
            {
                score += 10;
            }

            if (resource.Originals?.Any() == true)
            {
                score += 10;
            }

            return score;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="source"></param>
        /// <param name="target"></param>
        /// <returns>Changed</returns>
        public static bool MergeOnSynchronization(this ResourceDto source, ResourceDto target)
        {
            var changed = false;

            // Bad data from previous versions, not long-time support.
            if (source.RawName == source.RawFullname)
            {
                source.RawName = Path.GetFileName(source.RawName);
                changed = true;
            }

            if (target.IsSingleFile != source.IsSingleFile)
            {
                source.IsSingleFile = target.IsSingleFile;
                changed = true;
            }

            if (source.FileCreateDt != target.FileCreateDt ||
                source.FileModifyDt != target.FileModifyDt)
            {
                source.FileCreateDt = target.FileCreateDt;
                changed = true;
            }

            if (source.FileModifyDt != target.FileModifyDt)
            {
                source.FileModifyDt = target.FileModifyDt;
                changed = true;
            }

            if (source.Name.IsNullOrEmpty() && source.Name != target.Name && target.Name.IsNotEmpty())
            {
                source.Name = target.Name;
                changed = true;
            }

            if (target.Language != ResourceLanguage.NotSet && source.Language == ResourceLanguage.NotSet)
            {
                source.Language = target.Language;
                changed = true;
            }

            if (target.Rate > 0 && source.Rate == 0)
            {
                source.Rate = target.Rate;
                changed = true;
            }

            if (target.ReleaseDt.HasValue && !source.ReleaseDt.HasValue)
            {
                source.ReleaseDt = target.ReleaseDt.Value;
                changed = true;
            }

            if (source.Publishers?.Any() != true && target.Publishers?.Any() == true)
            {
                source.Publishers = target.Publishers;
                changed = true;
            }

            if (source.Series == null && target.Series != null)
            {
                source.Series = target.Series;
                changed = true;
            }

            if (source.Volume == null && target.Volume != null)
            {
                source.Volume = target.Volume;
                changed = true;
            }

            if (source.Originals?.Any() != true && target.Originals?.Any() == true)
            {
                source.Originals = target.Originals;
                changed = true;
            }

            if (target.Tags?.Any() == true)
            {
                source.Tags ??= new List<TagDto>();

                foreach (var t in target.Tags.Where(t =>
                             !source.Tags.Contains(t, TagDto.BizComparer)))
                {
                    source.Tags.Add(t);
                    changed = true;
                }
            }

            if (target.CustomProperties?.Any() == true)
            {
                source.CustomProperties ??= new Dictionary<string, List<CustomResourceProperty>>();

                foreach (var (k, cps) in target.CustomProperties)
                {
                    if (!source.CustomProperties.TryGetValue(k, out var pps))
                    {
                        source.CustomProperties[k] = pps = new List<CustomResourceProperty>();
                    }

                    foreach (var cp in cps.Where(cp =>
                                 !pps.Contains(cp, CustomResourceProperty.CustomResourcePropertyComparer)))
                    {
                        pps.Add(cp);
                        changed = true;
                    }
                }
            }

            if (source.Parent?.RawFullname != target.Parent?.RawFullname)
            {
                source.Parent = target.Parent;
                changed = true;
            }
            else
            {
                if (string.IsNullOrEmpty(source.Parent?.RawFullname) &&
                    string.IsNullOrEmpty(target.Parent?.RawFullname))
                {
                    if (source.ParentId.HasValue)
                    {
                        source.ParentId = null;
                        changed = true;
                    }
                }
            }

            if (source.MediaLibraryId != target.MediaLibraryId && target.MediaLibraryId > 0)
            {
                source.MediaLibraryId = target.MediaLibraryId;
                changed = true;
            }

            if (source.CategoryId != target.CategoryId && target.CategoryId > 0)
            {
                source.CategoryId = target.CategoryId;
                changed = true;
            }

            return changed;
        }

        /// <summary>
        /// Remove invalid and duplicate data
        /// </summary>
        /// <param name="resource"></param>
        public static void Clean(this ResourceDto resource)
        {
            if (resource.Publishers?.Any() == true)
            {
                resource.Publishers.RemoveInvalid();
            }

            if (resource.Originals?.Any() == true)
            {
                resource.Originals.RemoveAll(a => a.Name.IsNullOrEmpty() && a.Id == 0);
            }

            if (resource.Series != null)
            {
                if (resource.Series.Id == 0 && resource.Series.Name.IsNullOrEmpty())
                {
                    resource.Series = null;
                }
            }

            if (resource.Tags?.Any() == true)
            {
                resource.Tags.RemoveAll(a => a.Name.IsNullOrEmpty() && a.Id == 0);
            }
        }

        private static ResourceDiff? BuildDiff<T>(ResourceDiffProperty property, IEqualityComparer<T> comparer,
            object? oldValue, object? newValue) where T : class
        {
            if (!comparer.Equals(oldValue as T, newValue as T))
            {
                return new ResourceDiff
                {
                    Property = property,
                    CurrentValue = oldValue,
                    NewValue = newValue
                };
            }

            return null;
        }

        public static ResourceDiff? Compare(this ResourceDiffProperty property, object? a, object? b)
        {
            switch (property)
            {
                case ResourceDiffProperty.ReleaseDt:
                case ResourceDiffProperty.Name:
                case ResourceDiffProperty.Language:
                case ResourceDiffProperty.Introduction:
                case ResourceDiffProperty.Rate:
                {
                    return ResourceDiff.BuildRootDiff(property, a, b, EqualityComparer<object>.Default,
                        property.GetPropertyName(), null);
                }
                case ResourceDiffProperty.Volume:
                {
                    return ResourceDiff.BuildRootDiff(property, a as VolumeDto, b as VolumeDto, VolumeDto.BizComparer,
                        property.GetPropertyName(), VolumeExtensions.Compare);
                }
                case ResourceDiffProperty.Series:
                {
                    return ResourceDiff.BuildRootDiff(property, a as SeriesDto, b as SeriesDto, SeriesDto.BizComparer,
                        property.GetPropertyName(), SeriesExtensions.Compare);
                }
                case ResourceDiffProperty.Publisher:
                {
                    return ResourceDiff.BuildRootDiffForArrayProperty(property, a as List<PublisherDto>, b as List<PublisherDto>,
                        PublisherDto.BizComparer,
                        property.GetPropertyName(), PublisherExtensions.Compare);
                }
                case ResourceDiffProperty.Tag:
                {
                    return ResourceDiff.BuildRootDiffForArrayProperty(property, a as List<TagDto>, b as List<TagDto>,
                        TagDto.BizComparer, property.GetPropertyName(), TagExtensions.Compare);
                }
                case ResourceDiffProperty.Original:
                {
                    return ResourceDiff.BuildRootDiffForArrayProperty(property, a as List<OriginalDto>, b as List<OriginalDto>,
                        OriginalDto.BizComparer, property.GetPropertyName(),
                        OriginalExtensions.Compare);
                }
                case ResourceDiffProperty.CustomProperty:
                    return (a as List<CustomResourceProperty>).Compare(b as List<CustomResourceProperty>);
                case ResourceDiffProperty.Category:
                case ResourceDiffProperty.MediaLibrary:
                    return ResourceDiff.BuildRootDiff(property, (int) a!, (int) b!, IntEqualityComparer.Default, null, null);
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public static List<ResourceDiff> Compare(this ResourceDto a, ResourceDto b)
        {
            var diffs = new List<ResourceDiff?>();
            foreach (var property in SpecificEnumUtils<ResourceDiffProperty>.Values)
            {
                var getter = property.GetGetter();
                var va = getter(a);
                var vb = getter(b);
                diffs.Add(property.Compare(va, vb));
            }

            return diffs.Where(d => d != null).ToList()!;
        }


        #endregion

        #region Strings

        public static string BuildFullname(this ResourceDto resource, bool logical = false)
        {
            var releaseDate = resource.ReleaseDt.BuildReleaseDtString();
            var publisher = resource.Publishers.BuildPublisherString();
            var origin = resource.Originals.BuildOriginalsString();
            var language = resource.Language.BuildLanguageString();
            var volume = resource.Volume.BuildVolumeString();

            var fullname = $"{releaseDate}{publisher}{resource.Name ?? resource.RawName}{volume}{origin}{language}";

            return fullname;
        }

        public static string BuildVolumeString(this VolumeDto volume)
        {
            if (volume == null)
            {
                return null;
            }

            var str = $" {volume.Name}";

            if (volume.Title.IsNotEmpty())
            {
                str += $" {volume.Title}";
            }

            return str;
        }

        public static string BuildVolumeString(this string volume, bool includeSpace = true)
        {
            if (string.IsNullOrEmpty(volume))
            {
                return null;
            }

            if (includeSpace)
            {
                volume = $"{volume}";
            }

            return volume;
        }

        public static string BuildReleaseDtString(this DateTime? releaseDt, bool includeBracket = true)
        {
            var str = releaseDt?.ToString("yyMMdd");
            if (!string.IsNullOrEmpty(str))
            {
                if (includeBracket)
                {
                    str = $"[{str}]";
                }
            }

            return str;
        }

        public static string GetPropertyName(this ResourceDiffProperty property)
        {
            return property switch
            {
                ResourceDiffProperty.ReleaseDt => nameof(ResourceDto.ReleaseDt),
                ResourceDiffProperty.Name => nameof(ResourceDto.Name),
                ResourceDiffProperty.Language => nameof(ResourceDto.Language),
                ResourceDiffProperty.Introduction => nameof(ResourceDto.Introduction),
                ResourceDiffProperty.Rate => nameof(ResourceDto.Rate),
                ResourceDiffProperty.Volume => nameof(ResourceDto.Volume),
                ResourceDiffProperty.Original => nameof(ResourceDto.Originals),
                ResourceDiffProperty.Series => nameof(ResourceDto.Series),
                ResourceDiffProperty.Publisher => nameof(ResourceDto.Publishers),
                ResourceDiffProperty.Tag => nameof(ResourceDto.Tags),
                ResourceDiffProperty.CustomProperty => nameof(ResourceDto.CustomProperties),
                ResourceDiffProperty.Category => nameof(ResourceDto.CategoryId),
                ResourceDiffProperty.MediaLibrary => nameof(ResourceDto.MediaLibraryId),
                _ => throw new ArgumentOutOfRangeException(nameof(property), property, null)
            };
        }

        #endregion

        #region Converter

        public static ResourceDto ToDto(this Resource r)
        {
            if (r == null)
            {
                return null;
            }

            return new()
            {
                Id = r.Id,
                Name = r.Name,
                // Cover = r.Cover,
                // RawCover = r.RawCover
                // StartFiles = JsonConvert.DeserializeObject<string[]>(r.StartFiles ?? string.Empty),
                Directory = r.Directory.StandardizePath()!,
                Language = r.Language,
                Rate = r.Rate,
                ReleaseDt = r.ReleaseDt,
                RawName = r.RawName,
                CategoryId = r.CategoryId,
                MediaLibraryId = r.MediaLibraryId,
                CreateDt = r.CreateDt,
                UpdateDt = r.UpdateDt,
                FileCreateDt = r.FileCreateDt,
                FileModifyDt = r.FileModifyDt,
                IsSingleFile = r.IsSingleFile,
                Introduction = r.Introduction,
                HasChildren = r.HasChildren,
                ParentId = r.ParentId,
                // Originals = originals,
                // Publishers = publishers,
                // Series = series,
                // Tags = tags?.ToList(),
                // Volume = volume,
                // CustomProperties = customProperties?.GroupBy(t => t.Key).ToDictionary(t => t.Key, t => t.ToList()),
            };
        }

        public static Resource ToEntity(this ResourceDto r)
        {
            return new()
            {
                Id = r.Id,
                // Cover = r.Cover,
                // RawCover = r.RawCover
                // StartFiles = r.StartFiles.IsNotEmpty() ? JsonConvert.SerializeObject(r.StartFiles) : null,
                CreateDt = r.CreateDt,
                Directory = r.Directory,
                Language = r.Language,
                Name = r.Name,
                Rate = r.Rate,
                ReleaseDt = r.ReleaseDt,
                CategoryId = r.CategoryId,
                MediaLibraryId = r.MediaLibraryId,
                UpdateDt = r.UpdateDt,
                FileCreateDt = r.FileCreateDt,
                FileModifyDt = r.FileModifyDt,
                IsSingleFile = r.IsSingleFile,
                RawName = r.RawName,
                Introduction = r.Introduction,
                HasChildren = r.HasChildren,
                ParentId = r.Parent?.Id ?? r.ParentId
            };
        }

        public static ResourceSearchOptions ToOptions(this ResourceSearchDto dto)
        {
            if (dto == null)
            {
                return null;
            }

            return new ResourceSearchOptions
            {
	            AddEndDt = dto.AddEndDt,
	            AddStartDt = dto.AddStartDt,
	            CategoryId = dto.CategoryId,
	            CustomPropertyKeys = dto.CustomPropertyKeys,
	            CustomPropertyValues = dto.CustomPropertyKeys
		            ?.Select(a => dto.CustomProperties.TryGetValue(a, out var v) ? v : null).ToList(),
	            Everything = dto.Everything,
	            FavoritesIds = dto.FavoritesIds,
	            FileCreateEndDt = dto.FileCreateEndDt,
	            FileCreateStartDt = dto.FileCreateStartDt,
	            FileModifyEndDt = dto.FileModifyEndDt,
	            FileModifyStartDt = dto.FileModifyStartDt,
	            Languages = dto.Languages,
	            MediaLibraryIds = dto.MediaLibraryIds,
	            MinRate = dto.MinRate,
	            Name = dto.Name,
	            Orders = dto.Orders,
	            Original = dto.Original,
	            PageIndex = dto.PageIndex,
	            PageSize = dto.PageSize,
	            Publisher = dto.Publisher,
	            ReleaseEndDt = dto.ReleaseEndDt,
	            ReleaseStartDt = dto.ReleaseStartDt,
	            TagIds = dto.TagIds,
	            HideChildren = dto.HideChildren,
	            ExcludedTagIds = dto.ExcludedTagIds,
	            CustomPropertyIds = dto.CustomPropertyIds,
	            CustomProperties = dto.CustomPropertiesV2
            };
        }

        public static ResourceSearchDto? ToDto(this ResourceSearchOptions? options)
        {
            if (options == null)
            {
                return null;
            }

            var dto = new ResourceSearchDto
            {
                AddEndDt = options.AddEndDt,
                AddStartDt = options.AddStartDt,
                CategoryId = options.CategoryId,
                Everything = options.Everything,
                FavoritesIds = options.FavoritesIds,
                FileCreateEndDt = options.FileCreateEndDt,
                FileCreateStartDt = options.FileCreateStartDt,
                FileModifyEndDt = options.FileModifyEndDt,
                FileModifyStartDt = options.FileModifyStartDt,
                Languages = options.Languages,
                MediaLibraryIds = options.MediaLibraryIds,
                MinRate = options.MinRate,
                Name = options.Name,
                Orders = options.Orders,
                Original = options.Original,
                PageIndex = options.PageIndex,
                PageSize = options.PageSize,
                Publisher = options.Publisher,
                ReleaseEndDt = options.ReleaseEndDt,
                ReleaseStartDt = options.ReleaseStartDt,
                TagIds = options.TagIds,
                CustomPropertyKeys = options.CustomPropertyKeys,
                HideChildren = options.HideChildren, 
                CustomPropertiesV2 = options.CustomProperties, 
                ExcludedTagIds = options.ExcludedTagIds, 
                CustomPropertyIds = options.CustomPropertyIds
            };

            if (options.CustomPropertyKeys?.Any() == true)
            {
                var map = new Dictionary<string, string>();
                for (var i = 0; i < options.CustomPropertyKeys.Count; i++)
                {
                    var key = options.CustomPropertyKeys[i];
                    var value = options.CustomPropertyValues?.Count > i ? options.CustomPropertyValues[i] : null;
                    if (key.IsNotEmpty() && value.IsNotEmpty())
                    {
                        map[key] = value;
                    }
                }

                if (map.Any())
                {
                    dto.CustomProperties = map;
                }
            }

            return dto;
        }

        public static ResourceSearchSlotItemOptions ToOptions(this ResourceSearchSlotItemDto dto)
        {
            if (dto == null)
            {
                return null;
            }

            return new ResourceSearchSlotItemOptions
            {
                Name = dto.Name,
                Model = dto.Model.ToOptions()
            };
        }

        public static ResourceSearchSlotItemDto ToDto(this ResourceSearchSlotItemOptions options)
        {
            if (options == null)
            {
                return null;
            }

            return new ResourceSearchSlotItemDto
            {
                Name = options.Name,
                Model = options.Model.ToDto()
            };
        }

        /// <summary>
        /// Be cautious, <see cref="ResourceDto.Parent"/> will be cloned also.
        /// </summary>
        /// <param name="resource"></param>
        /// <returns></returns>
        public static ResourceDto Clone(this ResourceDto resource)
        {
            return new()
            {
                Id = resource.Id,
                Name = resource.Name,
                Language = resource.Language,
                Rate = resource.Rate,
                ReleaseDt = resource.ReleaseDt,
                RawName = resource.RawName,
                CategoryId = resource.CategoryId,
                MediaLibraryId = resource.MediaLibraryId,
                FileCreateDt = resource.FileCreateDt,
                FileModifyDt = resource.FileModifyDt,
                IsSingleFile = resource.IsSingleFile,
                Introduction = resource.Introduction,
                HasChildren = resource.HasChildren,
                ParentId = resource.ParentId,
                Originals = resource.Originals?.Select(OriginalExtensions.Clone).ToList(),
                Publishers = resource.Publishers?.Select(PublisherExtensions.Clone).ToList(),
                Series = resource.Series?.Clone(),
                Tags = resource.Tags?.Select(TagExtensions.Clone).ToList(),
                Volume = resource.Volume?.Clone(),
                CustomProperties = resource.CustomProperties?.ToDictionary(t => t.Key,
                    t => t.Value.Select(CustomPropertyExtensions.Clone).ToList()),
                CreateDt = resource.CreateDt,
                Directory = resource.Directory,
                Parent = resource.Parent?.Clone(),
                UpdateDt = resource.UpdateDt
            };
        }

        #endregion
    }
}