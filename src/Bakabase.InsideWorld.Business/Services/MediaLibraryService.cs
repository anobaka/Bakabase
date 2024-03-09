using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Serialization;
using Bakabase.InsideWorld.Business.Components.Resource.Components.PropertyMatcher;
using Bakabase.InsideWorld.Business.Components.Tasks;
using Bakabase.InsideWorld.Business.Configurations;
using Bakabase.InsideWorld.Business.Resources;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.InsideWorld.Models.Extensions;
using Bakabase.InsideWorld.Models.Models.Aos;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bakabase.InsideWorld.Models.Models.Entities.Implicit;
using Bakabase.InsideWorld.Models.RequestModels;
using Bootstrap.Components.Logging.LogService.Services;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Components.Orm;
using Bootstrap.Components.Orm.Infrastructures;
using Bootstrap.Components.Storage;
using Bootstrap.Extensions;
using Bootstrap.Models.Constants;
using Bootstrap.Models.ResponseModels;
using CsQuery.ExtensionMethods.Internal;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.DependencyModel;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.VisualBasic.FileIO;
using Newtonsoft.Json;
using NPOI.SS.Formula.Functions;
using SharpCompress.Readers;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using static Bakabase.InsideWorld.Models.Models.Aos.PathConfigurationValidateResult.Entry;
using SearchOption = System.IO.SearchOption;

namespace Bakabase.InsideWorld.Business.Services
{
	public class MediaLibraryService : ResourceService<InsideWorldDbContext, MediaLibrary, int>
	{
		private const decimal MinimalFreeSpace = 1_000_000_000;
		protected BackgroundTaskManager BackgroundTaskManager => GetRequiredService<BackgroundTaskManager>();
		protected ResourceCategoryService ResourceCategoryService => GetRequiredService<ResourceCategoryService>();
		protected ResourceService ResourceService => GetRequiredService<ResourceService>();
		protected TagService TagService => GetRequiredService<TagService>();
		protected BackgroundTaskHelper BackgroundTaskHelper => GetRequiredService<BackgroundTaskHelper>();
		private InsideWorldLocalizer _localizer;
		protected LogService LogService => GetRequiredService<LogService>();

		protected InsideWorldOptionsManagerPool InsideWorldAppService =>
			GetRequiredService<InsideWorldOptionsManagerPool>();

		public MediaLibraryService(IServiceProvider serviceProvider, InsideWorldLocalizer localizer) : base(
			serviceProvider)
		{
			_localizer = localizer;
		}

		public async Task<BaseResponse> Add(MediaLibraryCreateRequestModel model)
		{
			var dto = new MediaLibraryDto
			{
				Name = model.Name,
				CategoryId = model.CategoryId,
				PathConfigurations = model.PathConfigurations
			};

			var t = await Add(dto.ToEntity()!);
			return t;
		}

		public async Task AddRange(ICollection<MediaLibraryDto> mls)
		{
			var map = mls.ToDictionary(x => x, x => x.ToEntity()!);
			await base.AddRange(map.Values);
			foreach (var (dto, entity) in map)
			{
				dto.Id = entity.Id;
			}
		}

		public async Task<BaseResponse> Patch(int id, MediaLibraryPatchRequestModel model)
		{
			var ml = (await GetDto(id, MediaLibraryAdditionalItem.None))!;
			if (model.PathConfigurations != null)
			{
				foreach (var s in model.PathConfigurations!.Where(p => p.RpmValues?.Any() == true).SelectMany(p =>
					         p.RpmValues!.Where(s =>
						         s.Property != ResourceProperty.CustomProperty)))
				{
					s.Key = null;
				}

				ml.PathConfigurations = model.PathConfigurations;
			}

			if (ml.Name != model.Name && !string.IsNullOrEmpty(model.Name))
			{
				ml.Name = model.Name;
			}

			if (model.Order.HasValue)
			{
				ml.Order = model.Order.Value;
			}

			var t = await Update(ml.ToEntity()!);
			return t;
		}

		public async Task<BaseResponse> Put(MediaLibraryDto dto)
		{
			return await Update(dto.ToEntity()!);
		}

		public async Task<MediaLibraryDto?> GetDto(int id, MediaLibraryAdditionalItem additionalItems)
		{
			var ml = await GetByKey(id, true);
			return (await ToDtoList([ml], additionalItems)).FirstOrDefault();
		}

		public async Task<List<MediaLibraryDto>> GetAllDto(Expression<Func<MediaLibrary, bool>>? exp,
			MediaLibraryAdditionalItem additionalItems)
		{
			var wss = (await base.GetAll(exp, true)).OrderBy(a => a.Order).ToList();
			return await ToDtoList(wss, additionalItems);
		}

		protected async Task<List<MediaLibraryDto>> ToDtoList(List<MediaLibrary> mls,
			MediaLibraryAdditionalItem additionalItems)
		{
			var dtoList = mls.Select(ml => ml.ToDto()!).ToList();
			foreach (var ai in SpecificEnumUtils<MediaLibraryAdditionalItem>.Values)
			{
				if (additionalItems.HasFlag(ai))
				{
					switch (ai)
					{
						case MediaLibraryAdditionalItem.None:
							break;
						case MediaLibraryAdditionalItem.FileSystemInfo:
							{
								var drives = DriveInfo.GetDrives().ToDictionary(t => t.Name, t => t);
								var paths = dtoList.Where(ml => ml.PathConfigurations != null)
									.SelectMany(ml => ml.PathConfigurations!.Select(pc => pc.Path!))
									.Where(path => !string.IsNullOrEmpty(path)).Distinct();
								var fileSystemInfoMap = paths.ToDictionary(p => p, p =>
								{
									var ri = new MediaLibraryFileSystemInformation();
									var driveRoot = Path.GetPathRoot(p);
									if (drives.TryGetValue(driveRoot!, out var d))
									{
										ri.FreeSpace = d.AvailableFreeSpace;
										ri.TotalSize = d.TotalSize;
										if (d.AvailableFreeSpace < MinimalFreeSpace)
										{
											ri.Error = MediaLibraryFileSystemError.FreeSpaceNotEnough;
										}
									}
									else
									{
										ri.Error = MediaLibraryFileSystemError.InvalidVolume;
									}

									return ri;
								});
								foreach (var ml in dtoList)
								{
									ml.FileSystemInformation = ml.PathConfigurations
										?.Where(p => !string.IsNullOrEmpty(p.Path)).GroupBy(x => x.Path!)
										.ToDictionary(p => p.Key, p => fileSystemInfoMap.GetValueOrDefault(p.Key)!);
								}

								break;
							}
						case MediaLibraryAdditionalItem.FixedTags:
							{
								var fixedTagIds = dtoList.Where(t => t.PathConfigurations != null)
									.SelectMany(t =>
										t.PathConfigurations!.Where(a => a.FixedTagIds != null)
											.SelectMany(x => x.FixedTagIds!)).ToHashSet();
								var tags = (await TagService.GetByKeys(fixedTagIds, TagAdditionalItem.None))
									.ToDictionary(t => t.Id, t => t);
								foreach (var ml in dtoList.Where(t => t.PathConfigurations != null))
								{
									foreach (var pathConfiguration in ml.PathConfigurations!.Where(pathConfiguration =>
												 pathConfiguration.FixedTagIds?.Any() == true))
									{
										pathConfiguration.FixedTags = pathConfiguration.FixedTagIds!
											.Select(t => tags.GetValueOrDefault(t)).Where(t => t != null).ToList()!;
									}
								}

								break;
							}
						case MediaLibraryAdditionalItem.Category:
							{
								var categoryIds = dtoList.Select(c => c.CategoryId).ToHashSet();
								var categories =
									(await ResourceCategoryService.GetAllDto(x => categoryIds.Contains(x.Id))).ToDictionary(
										a => a.Id, a => a);
								foreach (var ml in dtoList)
								{
									ml.Category = categories.GetValueOrDefault(ml.CategoryId);
								}

								break;
							}
						default:
							throw new ArgumentOutOfRangeException();
					}
				}
			}

			return dtoList;
		}

		public async Task<BaseResponse> Sort(int[] ids)
		{
			var libraries = (await GetByKeys(ids)).ToDictionary(t => t.Id, t => t);
			var changed = new List<MediaLibrary>();
			for (var i = 0; i < ids.Length; i++)
			{
				var id = ids[i];
				if (libraries.TryGetValue(id, out var t) && t.Order != i)
				{
					t.Order = i;
					changed.Add(t);
				}
			}

			return await UpdateRange(changed);
		}

		public async Task<BaseResponse> Duplicate(int fromCategoryId, int toCategoryId)
		{
			var libraries = await GetAllDto(x => x.CategoryId == fromCategoryId, MediaLibraryAdditionalItem.None);
			if (libraries.Any())
			{
				var newLibraries = libraries.Select(l => l.Duplicate(toCategoryId)).ToArray();
				await AddRange(newLibraries);
			}

			return BaseResponseBuilder.Ok;
		}

		public async Task StopSync()
		{
			BackgroundTaskManager.StopByName(SyncTaskBackgroundTaskName);
		}

		public BackgroundTaskDto? SyncTaskInformation =>
			BackgroundTaskManager.GetByName(SyncTaskBackgroundTaskName).FirstOrDefault();

		public const string SyncTaskBackgroundTaskName = $"MediaLibraryService:Sync";

		private static string[] DiscoverAllResourceFullnameList(string rootPath, MatcherValue resourceMatcherValue,
			int maxCount = int.MaxValue)
		{
			rootPath = rootPath.StandardizePath()!;
			if (!rootPath.EndsWith(BusinessConstants.DirSeparator))
			{
				rootPath += BusinessConstants.DirSeparator;
			}

			var list = new List<string>();
			switch (resourceMatcherValue.ValueType)
			{
				case ResourceMatcherValueType.Layer:
				{
					var currentLayer = 0;
					var paths = new List<string> {rootPath};
					var nextLayerPaths = new List<string>();
					while (currentLayer++ < resourceMatcherValue.Layer! - 1)
					{
						var isTargetLayer = currentLayer == resourceMatcherValue.Layer - 1;
						foreach (var path in paths)
						{
							try
							{
								nextLayerPaths.AddRange(Directory.GetDirectories(path, "*",
									SearchOption.TopDirectoryOnly));
							}
							catch
							{
							}

							if (nextLayerPaths.Count > maxCount && isTargetLayer)
							{
								break;
							}
						}

						paths = nextLayerPaths;
						nextLayerPaths = new();
					}

					var allFileEntries = paths.SelectMany(p =>
					{
						try
						{
							return Directory.GetFileSystemEntries(p);
						}
						catch (Exception e)
						{
							return Array.Empty<string>();
						}
					});
					list.AddRange(allFileEntries.Select(s => s.StandardizePath()!));
					break;
				}
				case ResourceMatcherValueType.Regex:
				{
					var allEntries = Directory.GetFileSystemEntries(rootPath, "*", SearchOption.AllDirectories)
						.Select(e => e.StandardizePath()).OrderBy(a => a).ToArray();
					foreach (var e in allEntries)
					{
						var relativePath = e![rootPath.Length..];

						// ignore sub contents
						if (list.Any(l => e.StartsWith(l + BusinessConstants.DirSeparator)))
						{
							continue;
						}

						var match = Regex.Match(relativePath, resourceMatcherValue.Regex!);
						if (match.Success)
						{
							var length = match.Index + match.Value.Length;
							var matchedPath = relativePath[..length];

							if (matchedPath.SplitPathIntoSegments().Length ==
							    relativePath.SplitPathIntoSegments().Length)
							{
								list.Add(e);

								if (list.Count > maxCount)
								{
									break;
								}
							}
						}
					}

					break;
				}
				case ResourceMatcherValueType.FixedText:
				default:
					throw new ArgumentOutOfRangeException();
			}

			return list.Where(e => !BusinessConstants.IgnoredFileExtensions.Contains(Path.GetExtension(e))).ToArray();
		}

		/// <summary>
		/// 
		/// </summary>
		/// <returns></returns>
		public void SyncInBackgroundTask()
		{
			BackgroundTaskHelper.RunInNewScope<MediaLibraryService>(SyncTaskBackgroundTaskName,
				async (service, task) => await service.Sync(task));
		}

		private void SetPropertiesByMatchers(string rootPath, PathConfigurationValidateResult.Entry e, ResourceDto pr,
			Dictionary<string, ResourceDto> parentResources)
		{
			// var standardRootPath = rootPath.StandardizePath();
			// var otherMatchers = matchers.Where(a =>
			//     a.Property != ResourceProperty.RootPath && a.Property != ResourceProperty.Resource);
			// var rootPathSegments = standardRootPath.SplitPathIntoSegments();
			//
			// var segments = pr.RawFullname.SplitPathIntoSegments();
			// var matchedValues =
			//     new Dictionary<ResourceProperty, List<(MatcherValue MatcherValue, string Value)>>();
			// foreach (var s in otherMatchers)
			// {
			//     switch (s.Property)
			//     {
			//         case ResourceProperty.ParentResource:
			//         case ResourceProperty.ReleaseDt:
			//         case ResourceProperty.Name:
			//         case ResourceProperty.Volume:
			//         case ResourceProperty.Series:
			//         case ResourceProperty.Rate:
			//         case ResourceProperty.Original:
			//         case ResourceProperty.Publisher:
			//         case ResourceProperty.Tag:
			//         case ResourceProperty.CustomProperty:
			//         {
			//             var matchResult =
			//                 ResourcePropertyMatcher.Match(segments, s, rootPathSegments.Length - 1,
			//                     segments.Length - 1);
			//
			//
			//             if (matchResult != null)
			//             {
			//                 var values = matchedValues.GetOrAdd(s.Property, () => new());
			//
			//                 switch (matchResult.Type)
			//                 {
			//                     case MatchResultType.Layer:
			//                     {
			//                         // Value of parent resource should be a complete path
			//                         if (s.Property == ResourceProperty.ParentResource)
			//                         {
			//                             var prPath = string.Join(BusinessConstants.DirSeparator,
			//                                 segments.Take(matchResult.Index!.Value + 1));
			//                             values.Add((s, prPath));
			//                         }
			//                         else
			//                         {
			//                             values.Add((s, segments[matchResult.Index!.Value]));
			//                         }
			//
			//                         break;
			//                     }
			//                     case MatchResultType.Regex:
			//                     {
			//                         values.AddRange(matchResult.Matches!.Select(m => (s, m)));
			//                         break;
			//                     }
			//                     default:
			//                         throw new ArgumentOutOfRangeException();
			//                 }
			//             }
			//
			//             break;
			//         }
			//         case ResourceProperty.Introduction:
			//         case ResourceProperty.RootPath:
			//         case ResourceProperty.Resource:
			//         case ResourceProperty.Language:
			//             break;
			//         default:
			//             throw new ArgumentOutOfRangeException();
			//     }
			// }

			// property - custom key/string.empty - values
			// For Property=ParentResource, value will be a absolute path.
			var matchedValues = new Dictionary<ResourceProperty, Dictionary<string, List<string>>>();

			for (var i = 0; i < e.SegmentAndMatchedValues.Count; i++)
			{
				var t = e.SegmentAndMatchedValues[i];
				if (t.Properties.Any())
				{
					foreach (var p in t.Properties)
					{
						var keyAndValues = matchedValues.GetOrAdd(p.Property, () => new());
						if (p.Keys.Any())
						{
							foreach (var k in p.Keys)
							{
								var v = t.Value;
								if (p.Property == ResourceProperty.ParentResource)
								{
									v = Path.Combine(rootPath,
											string.Join(BusinessConstants.DirSeparator,
												e.SegmentAndMatchedValues.Take(i + 1).Select(a => a.Value)))
										.StandardizePath()!;
								}

								keyAndValues.GetOrAdd(k, () => new()).Add(v);
							}
						}
					}
				}
			}


			foreach (var t in e.GlobalMatchedValues)
			{
				var keyAndValues = matchedValues.GetOrAdd(t.Property, () => new());
				var values = keyAndValues.GetOrAdd(string.IsNullOrEmpty(t.Key) ? string.Empty : t.Key, () => new());
				values.AddRange(t.Values);
			}

			foreach (var (t, keyAndValues) in matchedValues)
			{
				foreach (var (key, values) in keyAndValues)
				{
					var firstValue = values.FirstOrDefault()!;
					switch (t)
					{
						case ResourceProperty.ParentResource:
						{
							if (firstValue != pr.RawFullname)
							{
								if (!parentResources.TryGetValue(firstValue, out var parent))
								{
									parentResources[firstValue] = parent = new ResourceDto
									{
										Directory = Path.GetDirectoryName(firstValue)!.StandardizePath()!,
										RawName = Path.GetFileName(firstValue),
										CategoryId = pr.CategoryId,
										MediaLibraryId = pr.MediaLibraryId,
										IsSingleFile = false
									};
								}

								pr.Parent = parent;
							}

							break;
						}
						case ResourceProperty.ReleaseDt:
						{
							DateTime dt = default;
							// todo: where does this key come from?
							if (!(key.IsNotEmpty() &&
							      DateTime.TryParseExact(firstValue, key, null, DateTimeStyles.None, out dt)) ||
							    DateTime.TryParse(firstValue, out dt))
							{
								pr.ReleaseDt = dt;
							}

							break;
						}
						case ResourceProperty.Publisher:
							pr.Publishers = values.Select(a => new PublisherDto {Name = a}).ToList();
							break;
						case ResourceProperty.Name:
							pr.Name = firstValue;
							break;
						case ResourceProperty.Volume:
							pr.Volume = new VolumeDto {Name = firstValue};
							break;
						case ResourceProperty.Original:
							pr.Originals = values.Select(a => new OriginalDto {Name = a}).ToList();
							break;
						case ResourceProperty.Series:
							pr.Series = new SeriesDto {Name = firstValue};
							break;
						case ResourceProperty.Tag:
							pr.Tags.AddRange(values.Select(a => new TagDto {Name = a}));
							break;
						case ResourceProperty.Rate:
							if (decimal.TryParse(firstValue, out var r))
							{
								pr.Rate = r;
							}

							break;
						case ResourceProperty.CustomProperty:
							pr.CustomProperties = keyAndValues.Where(a => !string.IsNullOrEmpty(a.Key))
								.ToDictionary(b => b.Key, c => c.Value.Select(b => new CustomResourceProperty
								{
									Value = b,
									ValueType = CustomDataType.String,
									// todo: this is a hard code to make it compatible with enhancer temporarily
									Key = $"p:{c.Key}"
								}).ToList());
							break;
						case ResourceProperty.Language:
						case ResourceProperty.Introduction:
						case ResourceProperty.RootPath:
						case ResourceProperty.Resource:
							break;
						default:
							throw new ArgumentOutOfRangeException();
					}
				}
			}
		}

		public async Task<BaseResponse> Sync(BackgroundTask task)
		{
			// Make log and error can show in background task info.
			var libraries = await GetAllDto(null, MediaLibraryAdditionalItem.None);
			var librariesMap = libraries.ToDictionary(a => a.Id, a => a);
			var categories = (await ResourceCategoryService.GetAllDto()).ToDictionary(a => a.Id, a => a);

			// Validation
			{
				var ignoredLibraries = new List<MediaLibraryDto>();
				foreach (var library in libraries)
				{
					if (!categories.TryGetValue(library.CategoryId, out var c))
					{
						await LogService.Log(SyncTaskBackgroundTaskName, LogLevel.Error, "CategoryValidationFailed",
							$"Media library [{library.Id}:{library.Name}] will not be synchronized because its category [id:{library.CategoryId}] is not found");
						ignoredLibraries.Add(library);
					}
				}

				libraries.RemoveAll(ignoredLibraries.Contains);
				// var paths = libraries.Where(t => t.PathConfigurations != null)
				//     .SelectMany(t => t.PathConfigurations.Select(b => b.Path)).ToArray();
				// if (CheckPathContaining(paths))
				// {
				//     throw new Exception(
				//         "Some paths of media libraries are related, please check them before syncing");
				// }
			}

			// Top level directory/file name - (Filename, IsSingleFile, MediaLibraryId, FixedTagIds, TagNames)
			var patchingResources = new Dictionary<string, ResourceDto>(StringComparer.OrdinalIgnoreCase);
			var parentResources = new Dictionary<string, ResourceDto>();
			var prevRawFullnameResourcesMap = new Dictionary<string, ResourceDto>();
			var invalidData = new List<ResourceDto>();

			var changedResources = new ConcurrentDictionary<string, ResourceDto>();

			var step = SpecificEnumUtils<MediaLibrarySyncStep>.Values.FirstOrDefault();

			while (step <= MediaLibrarySyncStep.SaveResources)
			{
				var basePercentage = step.GetBasePercentage();
				var stepPercentage = MediaLibrarySyncStepExtensions.Percentages[step];
				var resourceCountPerPercentage = patchingResources.Any()
					? patchingResources.Count / (decimal) stepPercentage
					: decimal.MaxValue;
				task.Percentage = basePercentage;
				task.CurrentProcess = step.ToString();
				switch (step)
				{
					case MediaLibrarySyncStep.Filtering:
					{
						var percentagePerLibrary =
							libraries.Count == 0 ? 0 : (decimal) 1 / libraries.Count * stepPercentage;
						for (var i = 0; i < libraries.Count; i++)
						{
							var library = libraries[i];
							if (library.PathConfigurations?.Any() != true)
							{
								continue;
							}

							var percentagePerPathConfiguration =
								(decimal) 1 / library.PathConfigurations.Count *
								percentagePerLibrary;
							for (var j = 0; j < library.PathConfigurations.Count; j++)
							{
								var pathConfiguration = library.PathConfigurations[j];
								var resourceMatcher =
									pathConfiguration.RpmValues.FirstOrDefault(m =>
										m.Property == ResourceProperty.Resource);
								if (!Directory.Exists(pathConfiguration.Path) || resourceMatcher == null)
								{
									continue;
								}

								var pscResult = await Test(pathConfiguration, int.MaxValue);
								if (pscResult.Code == (int) ResponseCode.Success)
								{
									var percentagePerItem =
										(decimal) 1 / pscResult.Data.Entries.Count * percentagePerPathConfiguration;
									var count = 0;
									foreach (var e in pscResult.Data.Entries)
									{
										var resourcePath =
											$"{pscResult.Data.RootPath}{BusinessConstants.DirSeparator}{e.RelativePath}";
										var pr = new ResourceDto()
										{
											CategoryId = library.CategoryId,
											MediaLibraryId = library.Id,
											IsSingleFile = new FileInfo(resourcePath).Exists,
											Directory = Path.GetDirectoryName(resourcePath).StandardizePath()!,
											RawName = Path.GetFileName(resourcePath),
											Tags = new List<TagDto>(),
										};

										if (pathConfiguration.FixedTagIds?.Any() == true)
										{
											pr.Tags.AddRange(
												pathConfiguration.FixedTagIds.Select(a =>
													new TagDto {Id = a}));
										}

										if (pathConfiguration.RpmValues?.Any() ==
										    true)
										{
											SetPropertiesByMatchers(pscResult.Data.RootPath, e, pr, parentResources);
										}

										patchingResources.TryAdd(pr.RawFullname, pr);
										task.Percentage = basePercentage + (int) (i * percentagePerLibrary +
											percentagePerPathConfiguration * j +
											percentagePerItem * (++count));
									}
								}

								// var standardRootPath = pathConfiguration.Path.StandardizePath();
								// var resourcePaths =
								//     DiscoverAllResourceFullnameList(pathConfiguration.Path, resourceMatcher);
								// if (!resourcePaths.Any())
								// {
								//     continue;
								// }

								// var otherMatchers = pathConfiguration.RpmValues
								// .Where(m => m != resourceMatcher && m.IsValid).ToList();
								// var percentagePerItem =
								//     (decimal) 1 / pscResult.Data.Entries.Count * percentagePerPathConfiguration;
								// var count = 0;
								// foreach (var resourcePath in resourcePaths)
								// {
								//     var pr = new ResourceDto()
								//     {
								//         CategoryId = library.CategoryId,
								//         MediaLibraryId = library.Id,
								//         IsSingleFile = new FileInfo(resourcePath).Exists,
								//         Directory = Path.GetDirectoryName(resourcePath).StandardizePath()!,
								//         RawName = Path.GetFileName(resourcePath),
								//         Tags = new List<TagDto>(),
								//     };
								//
								//     if (pathConfiguration.FixedTagIds?.Any() == true)
								//     {
								//         pr.Tags.AddRange(
								//             pathConfiguration.FixedTagIds.Select(a =>
								//                 new TagDto {Id = a}));
								//     }
								//
								//     if (pathConfiguration.RpmValues?.Any() ==
								//         true)
								//     {
								//         SetPropertiesByMatchers(pr, standardRootPath, otherMatchers, parentResources,
								//             library);
								//     }
								//
								//     patchingResources.TryAdd(pr.RawFullname, pr);
								//     task.Percentage = basePercentage + (int) (i * percentagePerLibrary +
								//                                               percentagePerPathConfiguration * j +
								//                                               percentagePerItem * (++count));
								// }
							}
						}

						break;
					}
					case MediaLibrarySyncStep.AcquireFileSystemInfo:
					{
						resourceCountPerPercentage = patchingResources.Any()
							? patchingResources.Count / (decimal) stepPercentage
							: decimal.MaxValue;
						var count = 0;
						foreach (var (fullname, patches) in patchingResources)
						{
							FileSystemInfo fileSystemInfo = patches.IsSingleFile
								? new FileInfo(fullname)
								: new DirectoryInfo(fullname);
							patches.FileCreateDt =
								fileSystemInfo.CreationTime.AddTicks(-(fileSystemInfo.CreationTime.Ticks %
								                                       TimeSpan.TicksPerSecond));
							patches.FileModifyDt =
								fileSystemInfo.LastWriteTime.AddTicks(-(fileSystemInfo.LastWriteTime.Ticks %
								                                        TimeSpan.TicksPerSecond));
							task.Percentage = basePercentage + (int) (++count / resourceCountPerPercentage);
						}

						break;
					}
					case MediaLibrarySyncStep.CleanResources:
					{
						// Remove resources belonged to unknown libraries
						await ResourceService.RemoveByMediaLibraryIdsNotIn(librariesMap.Keys.ToArray());
						var prevResources = await ResourceService.GetAll(ResourceAdditionalItem.All);

						var prevRawFullnameResourcesList = prevResources
							.GroupBy(a => a.RawFullname.StandardizePath(), StringComparer.OrdinalIgnoreCase)
							.ToDictionary(t => t.Key, t => t.ToArray());

						var duplicatedResources = prevRawFullnameResourcesList.Values.Where(t => t.Length > 0)
							.SelectMany(t => t.Skip(1)).ToArray();

						invalidData.AddRange(duplicatedResources);
						prevRawFullnameResourcesMap =
							prevRawFullnameResourcesList.ToDictionary(t => t.Key, t => t.Value[0]);

						// Compare
						var missingFullnameList = prevRawFullnameResourcesMap.Keys
							.Except(patchingResources.Keys, StringComparer.OrdinalIgnoreCase)
							.ToHashSet();
						// Remove unknown data
						// Bad directory/raw names will be deleted there.
						invalidData.AddRange(missingFullnameList.Select(a =>
						{
							prevRawFullnameResourcesMap.Remove(a, out var d);
							return d!;
						}));
						// Remove mismatched category/library resources.

						var invalidMediaLibraryResources = prevRawFullnameResourcesMap.Values
							.Where(a => !librariesMap.ContainsKey(a.MediaLibraryId)).ToArray();

						foreach (var r in invalidMediaLibraryResources)
						{
							prevRawFullnameResourcesMap.Remove(r.RawFullname);
							invalidData.Add(r);
						}

						await ResourceService.RemoveByKeys(invalidData.Select(a => a.Id).ToArray(), false);
						break;
					}
					case MediaLibrarySyncStep.CompareResources:
					{
						var count = 0;
						foreach (var (fullname, pr) in
						         patchingResources)
						{
							if (!prevRawFullnameResourcesMap.TryGetValue(fullname, out var resource))
							{
								resource = pr;
								changedResources[fullname] = resource;
							}

							if (resource.MergeOnSynchronization(pr))
							{
								changedResources[fullname] = resource;
							}

							task.Percentage = basePercentage + (int) (++count / resourceCountPerPercentage);
						}

						foreach (var (_, r) in changedResources)
						{
							r.UpdateDt = DateTime.Now;
						}

						break;
					}
					case MediaLibrarySyncStep.SaveResources:
					{
						var resourcesToBeSaved = changedResources.Values.ToList();
						var newResources = resourcesToBeSaved.Where(a => a.Id == 0).ToArray();
						await ResourceService.AddOrPatchRange(resourcesToBeSaved);

						// #region Tags
						//
						// var resourcePathIds = newResources.ToDictionary(t => t.RawFullname, t => t.Id);
						// foreach (var p in prevRawFullnameResourcesMap.Where(
						//              p => !resourcePathIds.ContainsKey(p.Key)))
						// {
						//     resourcePathIds[p.Key] = p.Value.Id;
						// }
						//
						// var newTagNames = patchingResources.Where(t => t.Value.DynamicTagNames != null)
						//     .SelectMany(t => t.Value.DynamicTagNames).Where(a => a.IsNotEmpty()).ToHashSet();
						// var newTagNameIdMap = new Dictionary<string, int>();
						// if (newTagNames.Any())
						// {
						//     newTagNameIdMap = (await TagService.AddRange(new Dictionary<string, string[]>
						//     {
						//         {
						//             string.Empty, newTagNames.ToArray()
						//         }
						//     }, true)).GroupBy(t => t.Name).ToDictionary(t => t.Key, t => t.FirstOrDefault()!.Id);
						// }
						//
						// var prevResourceIds = prevRawFullnameResourcesMap.Select(a => a.Value.Id).Where(a => a > 0)
						//     .Distinct()
						//     .ToArray();
						// var prevResourceTagMappings =
						//     (await ResourceTagMappingService.GetAll(a => prevResourceIds.Contains(a.ResourceId), false))
						//     .GroupBy(a => a.ResourceId).ToDictionary(a => a.Key,
						//         a => a.Select(b => b.TagId).Distinct().ToArray());
						//
						// var resourceIdTagIdsMap = new Dictionary<int, int[]>();
						// foreach (var (key, pr) in patchingResources.Where(t =>
						//              t.Value.DynamicTagNames != null || t.Value.FixedTagIds != null))
						// {
						//     var allTagIds = (pr.FixedTagIds ?? new int[] { })
						//         .Concat((pr.DynamicTagNames ?? new string[] { }).Select(t => newTagNameIdMap[t]))
						//         .Distinct()
						//         .ToArray();
						//     var resourceId = resourcePathIds[key];
						//     if (prevResourceTagMappings.TryGetValue(resourceId, out var prevTagIds))
						//     {
						//         allTagIds = allTagIds.Concat(prevTagIds).ToArray();
						//     }
						//
						//     if (allTagIds.Any())
						//     {
						//         resourceIdTagIdsMap[resourceId] = allTagIds.Distinct().ToArray();
						//     }
						// }
						//
						// await ResourceTagMappingService.UpdateRange(resourceIdTagIdsMap);
						//
						// #endregion

						// Update sync result
						var libraryResourceCount = patchingResources.GroupBy(a => a.Value.MediaLibraryId)
							.ToDictionary(a => a.Key, a => a.Count());
						await UpdateByKeys(libraries.Select(a => a.Id).ToArray(),
							l => { l.ResourceCount = libraryResourceCount.TryGetValue(l.Id, out var c) ? c : 0; });

						task.Message = string.Join(
							Environment.NewLine,
							$"[Resource] Found: {patchingResources.Count}, New: {newResources.Length} Removed: {invalidData.Count}, Updated: {resourcesToBeSaved.Count - newResources.Length}",
							$"[Directory]: Found: {patchingResources.Count(a => !a.Value.IsSingleFile)}",
							$"[SingleFile]: Found: {patchingResources.Count(a => a.Value.IsSingleFile)}"
						);
						task.Percentage = basePercentage + stepPercentage;

						await InsideWorldAppService.Resource.SaveAsync(t => t.LastSyncDt = DateTime.Now);

						break;
					}
					default:
						throw new ArgumentOutOfRangeException(nameof(step), step, null);
				}

				step++;
			}

			return BaseResponseBuilder.Ok;
		}

		public async Task<SingletonResponse<PathConfigurationValidateResult>> Test(
			PathConfigurationDto pc, int maxResourceCount = int.MaxValue)
		{
			if (pc.Path.IsNullOrEmpty())
			{
				return SingletonResponseBuilder<PathConfigurationValidateResult>.BuildBadRequest(
					_localizer.ValueIsNotSet(nameof(pc.Path)));
			}

			var resourceMatcherValue =
				pc.RpmValues.FirstOrDefault(a => a is
					{Property: ResourceProperty.Resource, IsValid: true});
			if (resourceMatcherValue == null)
			{
				return SingletonResponseBuilder<PathConfigurationValidateResult>.BuildBadRequest(
					"A valid resource matcher value is required");
			}

			pc.Path = pc.Path?.StandardizePath()!;
			var dir = new DirectoryInfo(pc.Path!);
			var entries = new List<PathConfigurationValidateResult.Entry>();
			if (dir.Exists)
			{
				var resourceFullnameList =
					DiscoverAllResourceFullnameList(pc.Path, resourceMatcherValue, maxResourceCount);

				var rootSegments = pc.Path!.SplitPathIntoSegments();
				foreach (var f in resourceFullnameList)
				{
					var segments = f.SplitPathIntoSegments();

					var relativeSegments = segments[rootSegments.Length..];
					var relativePath = string.Join(BusinessConstants.DirSeparator, relativeSegments);

					var otherMatchers = pc.RpmValues!.Where(a =>
						a.Property != ResourceProperty.Resource && a.Property != ResourceProperty.RootPath).ToList();

					// Index - Property - Custom Keys/String.Empty
					var tmpSegmentProperties = new Dictionary<int, Dictionary<ResourceProperty, List<string>>>();
					// Property - Custom Keys/String.Empty - Values
					var tmpGlobalMatchedValues =
						new Dictionary<ResourceProperty, Dictionary<string, List<string>>>();

					foreach (var m in otherMatchers)
					{
						var result = ResourcePropertyMatcher.Match(segments, m, rootSegments.Length - 1,
							segments.Length - 1);
						if (result != null)
						{
							switch (result.Type)
							{
								case MatchResultType.Layer:
								{
									var idx = result.Layer > 0
										? result.Layer.Value - 1
										: relativeSegments.Length + result.Layer!.Value - 1;

									var customKeys = tmpSegmentProperties
										.GetOrAdd(idx, () => new())
										.GetOrAdd(m.Property, () => new());
									customKeys.Add(!string.IsNullOrEmpty(m.Key) ? m.Key : string.Empty);

									break;
								}
								case MatchResultType.Regex:
								{
									var values = tmpGlobalMatchedValues
										.GetOrAdd(m.Property, () => new())
										.GetOrAdd(m.Key ?? string.Empty, () => new());
									values.AddRange(result.Matches!);

									break;
								}
								default:
									throw new ArgumentOutOfRangeException();
							}
						}
					}

					var list = new List<SegmentMatchResult>();

					for (var i = 0; i < relativeSegments.Length; i++)
					{
						var segment = relativeSegments[i];
						var segmentProperties = tmpSegmentProperties.TryGetValue(i, out var t)
							? t.Select(a => new SegmentMatchResult.SegmentPropertyResult(a.Key, a.Value)).ToList()
							: new List<SegmentMatchResult.SegmentPropertyResult>();

						var r = new SegmentMatchResult(segment, segmentProperties);
						list.Add(r);
					}

					var globalValues = tmpGlobalMatchedValues.SelectMany(a => a.Value.Select(b =>
						new GlobalMatchedValue(a.Key, b.Value, string.IsNullOrEmpty(b.Key) ? null : b.Key))).ToList();

					var entry = new PathConfigurationValidateResult.Entry(Directory.Exists(f), relativePath)
					{
						SegmentAndMatchedValues = list,
						IsDirectory = Directory.Exists(f),
						RelativePath = relativePath,
						GlobalMatchedValues = globalValues
					};

					entries.Add(entry);

					if (entries.Count >= maxResourceCount)
					{
						break;
					}
				}

				return new SingletonResponse<PathConfigurationValidateResult>(
					new PathConfigurationValidateResult(dir.FullName.StandardizePath()!, entries));
			}

			return SingletonResponseBuilder<PathConfigurationValidateResult>.NotFound;
		}

		public static bool CheckPathContaining(IReadOnlyCollection<string> pool, string target)
		{
			var stdPool = pool.Select(p => p.StandardizePath()!).ToList();
			var stdPath = target.StandardizePath()!;
			if (stdPool.Any(r => r.StartsWith(stdPath) || stdPath.StartsWith(r)))
			{
				return true;
			}

			return false;
		}
	}
}