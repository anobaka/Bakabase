// using System;
// using System.Collections.Generic;
// using System.Linq;
// using System.Text;
// using System.Text.RegularExpressions;
// using System.Threading.Tasks;
// using Bakabase.Abstractions.Components.Property;
// using Bakabase.Abstractions.Models.Domain;
// using Bakabase.Abstractions.Models.Domain.Constants;
// using Bakabase.InsideWorld.Business.Components.Legacy.Services;
// using Bakabase.InsideWorld.Business.Extensions;
// using Bakabase.InsideWorld.Business.Services;
// using Bakabase.InsideWorld.Models.Constants;
// using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
// using Bakabase.InsideWorld.Models.Extensions;
// using Bakabase.InsideWorld.Models.Models.Aos;
// using Bakabase.InsideWorld.Models.RequestModels;
// using Bakabase.Modules.Alias.Abstractions.Services;
// using Bakabase.Modules.Alias.Models.Input;
// using Bakabase.Modules.Property.Abstractions.Components;
// using Bakabase.Modules.Property.Abstractions.Services;
// using Bakabase.Modules.Property.Components;
// using Bakabase.Modules.Property.Extensions;
// using Bakabase.Modules.StandardValue.Abstractions.Components;
// using Bakabase.Modules.StandardValue.Extensions;
// using Newtonsoft.Json;
// using SQLitePCL;
//
// namespace Bakabase.InsideWorld.Business.Components.Search
// {
//     public class DefaultResourceSearchContextProcessor(
//         ICustomPropertyValueService customPropertyValueService,
//         IAliasService aliasService,
//         ICustomPropertyService customPropertyService)
//         : IResourceSearchContextProcessor
//     {
//         private async Task PrepareAliases(ResourceSearchFilter filter, ResourceSearchContext context)
//         {
//             if (filter.PropertyPool == PropertyPool.Custom)
//             {
//                 var property = context.PropertiesDataPool?.GetValueOrDefault(filter.PropertyId);
//                 if (property != null)
//                 {
//                     if (property.Type.IntegratedWithAlias())
//                     {
//                         var allAliasGroups =
//                             (await aliasService.SearchGroups(new AliasSearchInputModel {PageSize = int.MaxValue}))
//                             .Data;
//
//                         context.AliasCandidates ??= allAliasGroups.SelectMany(g =>
//                         {
//                             var allTexts = g.Candidates ?? [];
//                             allTexts.Add(g.Text);
//                             return allTexts.Select(text => (Text: text, Candidates: allTexts));
//                         }).ToDictionary(d => d.Text, d => d.Candidates);
//                     }
//                 }
//             }
//         }
//
// 		private async Task PrepareCustomProperties(ResourceSearchContext context)
// 		{
// 			context.PropertiesDataPool ??=
// 				(await customPropertyService.GetAll(null, CustomPropertyAdditionalItem.None, false))
// 				.ToDictionary(x => x.Id, x => x);
// 		}
//
//         private async Task<Dictionary<int, List<CustomPropertyValue>?>?> PrepareAndGetCustomPropertyValues(
//             ResourceSearchFilter filter, ResourceSearchContext context)
//         {
//             context.CustomPropertyDataPool ??= new();
//
//             if (!context.CustomPropertyDataPool.TryGetValue(filter.PropertyId, out var propertyValues))
//             {
//                 var rawValues = await customPropertyValueService.GetAll(x => x.PropertyId == filter.PropertyId,
//                     CustomPropertyValueAdditionalItem.None, false);
//                 propertyValues = rawValues.GroupBy(x => x.ResourceId)
//                     .ToDictionary(x => x.Key, List<CustomPropertyValue>? (x) => x.ToList());
//                 var nullValueIds = context.AllResourceIds.Except(propertyValues.Keys);
//                 foreach (var id in nullValueIds)
//                 {
//                     propertyValues[id] = null;
//                 }
//
//                 context.CustomPropertyDataPool[filter.PropertyId] = propertyValues;
//
//             }
//
//             return propertyValues;
//         }
//
//         public async Task<HashSet<int>?> Search(ResourceSearchFilter filter, ResourceSearchContext context)
//         {
// 			HashSet<int>? set = null;
// 			if (filter.PropertyId != 0 && filter.Operation != 0)
// 			{
// 				if (filter.PropertyPool != PropertyPool.Custom)
// 				{
// 					var property = (SearchableReservedProperty) filter.PropertyId;
// 					switch (property)
// 					{
// 						case SearchableReservedProperty.FileName:
// 						case SearchableReservedProperty.DirectoryPath:
//                         {
//                             var filterValue = string.IsNullOrEmpty(filter.DbValue)
//                                 ? null
//                                 : filter.DbValue?.DeserializeAsStandardValue<string>(StandardValueType.String);
//
// 							var getValue = property switch
// 							{
// 								SearchableReservedProperty.FileName =>
// 									(Func<Abstractions.Models.Domain.Resource, string>) (x => x.FileName),
// 								SearchableReservedProperty.DirectoryPath => x => x.Directory!,
// 								_ => null!
// 							};
//
// 							switch (filter.Operation)
// 							{
// 								case SearchOperation.Equals:
// 								{
// 									if (!string.IsNullOrEmpty(filterValue))
// 									{
// 										set = context.ResourcesPool?.Where(x => filterValue.Equals(getValue(x.Value)))
// 											.Select(x => x.Key).ToHashSet() ?? [];
// 									}
//
// 									break;
// 								}
// 								case SearchOperation.NotEquals:
// 								{
// 									if (!string.IsNullOrEmpty(filterValue))
// 									{
// 										var ids = context.ResourcesPool
// 											?.Where(x => filterValue.Equals(getValue(x.Value)))
// 											.Select(x => x.Key).ToHashSet();
// 										if (ids?.Any() == true)
// 										{
// 											set = context.AllResourceIds.Except(ids).ToHashSet();
// 										}
// 									}
//
// 									break;
// 								}
// 								case SearchOperation.Contains:
// 								{
// 									if (!string.IsNullOrEmpty(filterValue))
// 									{
// 										set = context.ResourcesPool?.Where(x => getValue(x.Value).Contains(filterValue))
// 											.Select(x => x.Key).ToHashSet() ?? [];
// 									}
//
// 									break;
// 								}
// 								case SearchOperation.NotContains:
// 								{
// 									if (!string.IsNullOrEmpty(filterValue))
// 									{
// 										var ids = context.ResourcesPool
// 											?.Where(x => getValue(x.Value).Contains(filterValue))
// 											.Select(x => x.Key).ToHashSet();
// 										if (ids?.Any() == true)
// 										{
// 											set = context.AllResourceIds.Except(ids).ToHashSet();
// 										}
// 									}
//
// 									break;
// 								}
// 								case SearchOperation.StartsWith:
// 								{
// 									if (!string.IsNullOrEmpty(filterValue))
// 									{
// 										set = context.ResourcesPool
// 											?.Where(x => getValue(x.Value).StartsWith(filterValue))
// 											.Select(x => x.Key).ToHashSet() ?? [];
// 									}
//
// 									break;
// 								}
// 								case SearchOperation.NotStartsWith:
// 								{
// 									if (!string.IsNullOrEmpty(filterValue))
// 									{
// 										var ids = context.ResourcesPool
// 											?.Where(x => getValue(x.Value).StartsWith(filterValue))
// 											.Select(x => x.Key).ToHashSet();
// 										if (ids?.Any() == true)
// 										{
// 											set = context.AllResourceIds.Except(ids).ToHashSet();
// 										}
// 									}
//
// 									break;
// 								}
// 								case SearchOperation.EndsWith:
// 								{
// 									if (!string.IsNullOrEmpty(filterValue))
// 									{
// 										set = context.ResourcesPool?.Where(x => getValue(x.Value).EndsWith(filterValue))
// 											.Select(x => x.Key).ToHashSet() ?? [];
// 									}
//
// 									break;
// 								}
// 								case SearchOperation.NotEndsWith:
// 								{
// 									if (!string.IsNullOrEmpty(filterValue))
// 									{
// 										var ids = context.ResourcesPool
// 											?.Where(x => !getValue(x.Value).EndsWith(filterValue))
// 											.Select(x => x.Key).ToHashSet();
// 										if (ids?.Any() == true)
// 										{
// 											set = context.AllResourceIds.Except(ids).ToHashSet();
// 										}
// 									}
//
// 									break;
// 								}
// 								case SearchOperation.IsNull:
// 								{
// 									var ids = context.ResourcesPool?.Select(x => x.Key).ToHashSet();
// 									if (ids?.Any() == true)
// 									{
// 										set = context.AllResourceIds.Except(ids).ToHashSet();
// 									}
//
// 									break;
// 								}
// 								case SearchOperation.IsNotNull:
// 								{
// 									set = context.ResourcesPool?.Select(x => x.Key).ToHashSet() ?? [];
// 									break;
// 								}
// 								case SearchOperation.Matches:
// 								{
// 									if (!string.IsNullOrEmpty(filterValue))
// 									{
// 										var regex = new Regex(filterValue);
// 										set = context.ResourcesPool?.Where(x => regex.IsMatch(getValue(x.Value)))
// 											.Select(x => x.Key).ToHashSet() ?? [];
// 									}
//
// 									break;
// 								}
// 								case SearchOperation.NotMatches:
// 								{
// 									if (!string.IsNullOrEmpty(filterValue))
// 									{
// 										var regex = new Regex(filterValue);
// 										var ids = context.ResourcesPool?.Where(x => regex.IsMatch(getValue(x.Value)))
// 											.Select(x => x.Key).ToHashSet();
// 										if (ids?.Any() == true)
// 										{
// 											set = context.AllResourceIds.Except(ids).ToHashSet();
// 										}
// 									}
//
// 									break;
// 								}
// 								default:
// 									throw new ArgumentOutOfRangeException();
// 							}
//
// 							break;
// 						}
// 						case SearchableReservedProperty.CreatedAt:
// 						// case SearchableReservedProperty.ModifiedAt:
// 						case SearchableReservedProperty.FileCreatedAt:
// 						case SearchableReservedProperty.FileModifiedAt:
//                         {
//                             var filterValue = string.IsNullOrEmpty(filter.DbValue)
//                                 ? null
//                                 : filter.DbValue?.DeserializeAsStandardValue<DateTime>(StandardValueType.DateTime);
//
// 							var getValue = property switch
// 							{
// 								SearchableReservedProperty.CreatedAt =>
// 									(Func<Abstractions.Models.Domain.Resource, DateTime?>) (x => x.CreatedAt),
// 								SearchableReservedProperty.FileCreatedAt => x => x.FileCreatedAt,
// 								SearchableReservedProperty.FileModifiedAt => x => x.FileModifiedAt,
// 								_ => null!
// 							};
//
// 							switch (filter.Operation)
// 							{
// 								case SearchOperation.Equals:
// 								{
// 									if (filterValue.HasValue)
// 									{
// 										set = context.ResourcesPool?.Where(x => filterValue == getValue(x.Value))
// 											.Select(x => x.Key).ToHashSet() ?? [];
// 									}
//
// 									break;
// 								}
// 								case SearchOperation.NotEquals:
// 								{
// 									if (filterValue.HasValue)
// 									{
// 										var ids = context.ResourcesPool?.Where(x => filterValue == getValue(x.Value))
// 											.Select(x => x.Key).ToHashSet();
// 										if (ids?.Any() == true)
// 										{
// 											set = context.AllResourceIds.Except(ids).ToHashSet();
// 										}
// 									}
//
// 									break;
// 								}
// 								case SearchOperation.GreaterThan:
// 								{
// 									if (filterValue.HasValue)
// 									{
// 										set = context.ResourcesPool?.Where(x => filterValue > getValue(x.Value))
// 											.Select(x => x.Key).ToHashSet() ?? [];
// 									}
//
// 									break;
// 								}
// 								case SearchOperation.LessThan:
// 								{
// 									if (filterValue.HasValue)
// 									{
// 										set = context.ResourcesPool?.Where(x => filterValue < getValue(x.Value))
// 											.Select(x => x.Key).ToHashSet() ?? [];
// 									}
//
// 									break;
// 								}
// 								case SearchOperation.GreaterThanOrEquals:
// 								{
// 									if (filterValue.HasValue)
// 									{
// 										set = context.ResourcesPool?.Where(x => filterValue >= getValue(x.Value))
// 											.Select(x => x.Key).ToHashSet() ?? [];
// 									}
//
// 									break;
// 								}
// 								case SearchOperation.LessThanOrEquals:
// 								{
// 									if (filterValue.HasValue)
// 									{
// 										set = context.ResourcesPool?.Where(x => filterValue <= getValue(x.Value))
// 											.Select(x => x.Key).ToHashSet() ?? [];
// 									}
//
// 									break;
// 								}
// 								case SearchOperation.IsNull:
// 								{
// 									var ids = context.ResourcesPool?.Select(x => x.Key).ToHashSet();
// 									if (ids?.Any() == true)
// 									{
// 										set = context.AllResourceIds.Except(ids).ToHashSet();
// 									}
//
// 									break;
// 								}
// 								case SearchOperation.IsNotNull:
// 								{
// 									set = context.ResourcesPool?.Select(x => x.Key).ToHashSet() ?? [];
// 									break;
// 								}
// 								default:
// 									throw new ArgumentOutOfRangeException();
// 							}
//
// 							break;
// 						}
//                         case SearchableReservedProperty.MediaLibrary:
//                         case SearchableReservedProperty.Category:
//                         {
//                             var getValue = property switch
//                             {
//                                 SearchableReservedProperty.MediaLibrary => (Func<Abstractions.Models.Domain.Resource, int>)
//                                     (x => x.MediaLibraryId),
//                                 SearchableReservedProperty.Category => x => x.CategoryId,
//                                 _ => null!
//                             };
//
//                             switch (filter.Operation)
//                             {
//                                 case SearchOperation.Equals:
//                                 case SearchOperation.NotEquals:
//                                 {
//                                     var filterValue =
//                                         filter.DbValue?.DeserializeAsStandardValue<decimal>(StandardValueType.Decimal)
//                                             is { } d
//                                             ? (int?) d
//                                             : null;
//                                     switch (filter.Operation)
//                                     {
//                                         case SearchOperation.Equals:
//                                         {
//                                             if (filterValue.HasValue)
//                                             {
//                                                 set = context.ResourcesPool
//                                                     ?.Where(x => filterValue == getValue(x.Value))
//                                                     .Select(x => x.Key).ToHashSet() ?? [];
//                                             }
//
//                                             break;
//                                         }
//                                         case SearchOperation.NotEquals:
//                                         {
//                                             if (filterValue.HasValue)
//                                             {
//                                                 var ids = context.ResourcesPool
//                                                     ?.Where(x => filterValue == getValue(x.Value))
//                                                     .Select(x => x.Key).ToHashSet();
//                                                 if (ids?.Any() == true)
//                                                 {
//                                                     set = context.AllResourceIds.Except(ids).ToHashSet();
//                                                 }
//                                             }
//
//                                             break;
//                                         }
//                                         default:
//                                             throw new ArgumentOutOfRangeException();
//                                     }
//
//                                     break;
//                                 }
//                                 case SearchOperation.In:
//                                 case SearchOperation.NotIn:
//                                 {
//                                     var filterValue = filter.DbValue?.DeserializeAsStandardValue<List<string>>(StandardValueType.ListString)?.Select(int.Parse).ToList();
//                                             
//                                     switch (filter.Operation)
//                                     {
//                                         case SearchOperation.In:
//                                         {
//                                             if (filterValue?.Any() == true)
//                                             {
//                                                 return context.ResourcesPool
//                                                     ?.Where(x => filterValue.Contains(getValue(x.Value)))
//                                                     .Select(x => x.Key).ToHashSet();
//                                             }
//
//                                             break;
//                                         }
//                                         case SearchOperation.NotIn:
//                                         {
//                                             if (filterValue?.Any() == true)
//                                             {
//                                                 return context.ResourcesPool
//                                                     ?.Where(x => !filterValue.Contains(getValue(x.Value)))
//                                                     .Select(x => x.Key).ToHashSet();
//                                             }
//
//                                             break;
//                                         }
//                                         default:
//                                             throw new ArgumentOutOfRangeException();
//                                     }
//
//                                     break;
//                                 }
//                                 default:
//                                     throw new ArgumentOutOfRangeException();
//                             }
//
//                             break;
//                         }
//                         default:
// 							throw new ArgumentOutOfRangeException();
// 					}
// 				}
// 				else
// 				{
// 					await PrepareCustomProperties(context);
//
//                     await PrepareAliases(filter, context);
//
//                     var property = context.PropertiesDataPool![filter.PropertyId];
//                     var propertyValues = await PrepareAndGetCustomPropertyValues(filter, context);
//
//                     var filterValueType = PropertyInternals.DescriptorMap.GetValueOrDefault(property.Type)
//                         ?.SearchOperations.GetValueOrDefault(filter.Operation)?.RenderAs.GetBizValueType();
//
//                     var filterValue = filterValueType.HasValue ? filter.DbValue?.DeserializeAsStandardValue(filterValueType.Value) : null;
//
//                     if (PropertyInternals.DescriptorMap.TryGetValue(property.Type, out var descriptor))
//                     {
//                         set = propertyValues
//                             ?.Where(x =>
//                                 x.Value?.Any(y => descriptor.IsMatch(y.Value, filter.Operation, filterValue)) == true)
//                             .Select(x => x.Key).ToHashSet() ?? [];
//                     }
//                 }
//             }
//
// 			return set;
// 		}
// 	}
// }