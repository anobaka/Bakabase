using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Business.Extensions;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.InsideWorld.Models.Extensions;
using Bakabase.InsideWorld.Models.Models.Aos;
using Bakabase.InsideWorld.Models.RequestModels;
using Bakabase.Modules.CustomProperty.Abstractions;
using Newtonsoft.Json;
using SQLitePCL;

namespace Bakabase.InsideWorld.Business.Components.Search
{
	public class DefaultResourceSearchContextProcessor : IResourceSearchContextProcessor
	{
		private readonly CustomPropertyValueService _customPropertyValueService;
		private readonly AliasService _aliasService;
		private readonly CustomPropertyService _customPropertyService;
		private readonly FavoritesResourceMappingService _favoritesResourceMappingService;
		private readonly ResourceTagMappingService _resourceTagMappingService;
        private readonly Dictionary<int, ICustomPropertyDescriptor> _propertyDescriptors;

		public DefaultResourceSearchContextProcessor(CustomPropertyValueService customPropertyValueService,
			AliasService aliasService, CustomPropertyService customPropertyService, FavoritesResourceMappingService favoritesResourceMappingService, ResourceTagMappingService resourceTagMappingService, IEnumerable<ICustomPropertyDescriptor> propertyDescriptors)
		{
			_customPropertyValueService = customPropertyValueService;
			_aliasService = aliasService;
			_customPropertyService = customPropertyService;
			_favoritesResourceMappingService = favoritesResourceMappingService;
			_resourceTagMappingService = resourceTagMappingService;
            _propertyDescriptors = propertyDescriptors.ToDictionary(x => x.Type);
        }

		private async Task PrepareAliases(ResourceSearchFilter filter, ResourceSearchContext context)
        {
            throw new NotImplementedException();
            // context.Aliases ??= await _aliasService.GetFullMap();
        }

		private async Task PrepareCustomProperties(ResourceSearchContext context)
		{
			context.PropertiesDataPool ??=
				(await _customPropertyService.GetAll(null, CustomPropertyAdditionalItem.None, false))
				.ToDictionary(x => x.Id, x => x);
		}

		private async Task PrepareFavorites(ResourceSearchContext context)
		{
			if (context.FavoritesResourceDataPool == null)
			{
				var favoritesMappings = await _favoritesResourceMappingService.GetAll(null, false);
				context.FavoritesResourceDataPool = favoritesMappings.GroupBy(x => x.FavoritesId)
					.ToDictionary(x => x.Key, x => x.Select(y => y.ResourceId).ToHashSet());
			}
		}

		private async Task PrepareTags(ResourceSearchContext context)
		{
			if (context.TagResourceDataPool == null)
			{
				var tagMappings = await _resourceTagMappingService.GetAll(null, false);
				context.TagResourceDataPool = tagMappings.GroupBy(x => x.TagId)
					.ToDictionary(x => x.Key, x => x.Select(y => y.ResourceId).ToHashSet());
			}
		}

        private async Task<Dictionary<int, CustomPropertyValue?>?> PrepareAndGetCustomPropertyValues(
            ResourceSearchFilter filter, ResourceSearchContext context)
        {
            context.CustomPropertyDataPool ??= new();

            if (!context.CustomPropertyDataPool.TryGetValue(filter.PropertyId, out var propertyValues))
            {
                var rawValues = await _customPropertyValueService.GetAll(x => x.PropertyId == filter.PropertyId,
                    CustomPropertyValueAdditionalItem.None, false);
                propertyValues = rawValues.ToDictionary(x => x.ResourceId, x => (CustomPropertyValue?) x);
                var nullValueIds = context.AllResourceIds.Except(propertyValues.Keys);
                foreach (var id in nullValueIds)
                {
                    propertyValues[id] = null;
                }

                context.CustomPropertyDataPool[filter.PropertyId] = propertyValues;

            }

            return propertyValues;
        }

        public async Task<HashSet<int>?> Search(ResourceSearchFilter filter, ResourceSearchContext context)
		{
			HashSet<int>? set = null;
			if (filter.PropertyId != 0 && filter.Operation != 0)
			{
				await PrepareAliases(filter, context);

				if (filter.IsReservedProperty)
				{
					var property = (SearchableReservedProperty) filter.PropertyId;
					switch (property)
					{
						case SearchableReservedProperty.FileName:
						case SearchableReservedProperty.DirectoryPath:
						{
							var filterValue = string.IsNullOrEmpty(filter.Value)
								? null
								: JsonConvert.DeserializeObject<string>(filter.Value);

							var getValue = property switch
							{
								SearchableReservedProperty.FileName =>
									(Func<Abstractions.Models.Db.Resource, string>) (x => x.RawName),
								SearchableReservedProperty.DirectoryPath => x => x.Directory!,
								_ => null!
							};

							switch (filter.Operation)
							{
								case SearchOperation.Equals:
								{
									if (!string.IsNullOrEmpty(filterValue))
									{
										set = context.ResourcesPool?.Where(x => filterValue.Equals(getValue(x.Value)))
											.Select(x => x.Key).ToHashSet() ?? [];
									}

									break;
								}
								case SearchOperation.NotEquals:
								{
									if (!string.IsNullOrEmpty(filterValue))
									{
										var ids = context.ResourcesPool
											?.Where(x => filterValue.Equals(getValue(x.Value)))
											.Select(x => x.Key).ToHashSet();
										if (ids?.Any() == true)
										{
											set = context.AllResourceIds.Except(ids).ToHashSet();
										}
									}

									break;
								}
								case SearchOperation.Contains:
								{
									if (!string.IsNullOrEmpty(filterValue))
									{
										set = context.ResourcesPool?.Where(x => getValue(x.Value).Contains(filterValue))
											.Select(x => x.Key).ToHashSet() ?? [];
									}

									break;
								}
								case SearchOperation.NotContains:
								{
									if (!string.IsNullOrEmpty(filterValue))
									{
										var ids = context.ResourcesPool
											?.Where(x => getValue(x.Value).Contains(filterValue))
											.Select(x => x.Key).ToHashSet();
										if (ids?.Any() == true)
										{
											set = context.AllResourceIds.Except(ids).ToHashSet();
										}
									}

									break;
								}
								case SearchOperation.StartsWith:
								{
									if (!string.IsNullOrEmpty(filterValue))
									{
										set = context.ResourcesPool
											?.Where(x => getValue(x.Value).StartsWith(filterValue))
											.Select(x => x.Key).ToHashSet() ?? [];
									}

									break;
								}
								case SearchOperation.NotStartsWith:
								{
									if (!string.IsNullOrEmpty(filterValue))
									{
										var ids = context.ResourcesPool
											?.Where(x => getValue(x.Value).StartsWith(filterValue))
											.Select(x => x.Key).ToHashSet();
										if (ids?.Any() == true)
										{
											set = context.AllResourceIds.Except(ids).ToHashSet();
										}
									}

									break;
								}
								case SearchOperation.EndsWith:
								{
									if (!string.IsNullOrEmpty(filterValue))
									{
										set = context.ResourcesPool?.Where(x => getValue(x.Value).EndsWith(filterValue))
											.Select(x => x.Key).ToHashSet() ?? [];
									}

									break;
								}
								case SearchOperation.NotEndsWith:
								{
									if (!string.IsNullOrEmpty(filterValue))
									{
										var ids = context.ResourcesPool
											?.Where(x => !getValue(x.Value).EndsWith(filterValue))
											.Select(x => x.Key).ToHashSet();
										if (ids?.Any() == true)
										{
											set = context.AllResourceIds.Except(ids).ToHashSet();
										}
									}

									break;
								}
								case SearchOperation.IsNull:
								{
									var ids = context.ResourcesPool?.Select(x => x.Key).ToHashSet();
									if (ids?.Any() == true)
									{
										set = context.AllResourceIds.Except(ids).ToHashSet();
									}

									break;
								}
								case SearchOperation.IsNotNull:
								{
									set = context.ResourcesPool?.Select(x => x.Key).ToHashSet() ?? [];
									break;
								}
								case SearchOperation.Matches:
								{
									if (!string.IsNullOrEmpty(filterValue))
									{
										var regex = new Regex(filterValue);
										set = context.ResourcesPool?.Where(x => regex.IsMatch(getValue(x.Value)))
											.Select(x => x.Key).ToHashSet() ?? [];
									}

									break;
								}
								case SearchOperation.NotMatches:
								{
									if (!string.IsNullOrEmpty(filterValue))
									{
										var regex = new Regex(filterValue);
										var ids = context.ResourcesPool?.Where(x => regex.IsMatch(getValue(x.Value)))
											.Select(x => x.Key).ToHashSet();
										if (ids?.Any() == true)
										{
											set = context.AllResourceIds.Except(ids).ToHashSet();
										}
									}

									break;
								}
								default:
									throw new ArgumentOutOfRangeException();
							}

							break;
						}
						case SearchableReservedProperty.CreatedAt:
						// case SearchableReservedProperty.ModifiedAt:
						case SearchableReservedProperty.FileCreatedAt:
						case SearchableReservedProperty.FileModifiedAt:
						{
							var filterValue = string.IsNullOrEmpty(filter.Value)
								? null
								: JsonConvert.DeserializeObject<DateTime?>(filter.Value);

							var getValue = property switch
							{
								SearchableReservedProperty.CreatedAt =>
									(Func<Abstractions.Models.Db.Resource, DateTime?>) (x => x.CreateDt),
								SearchableReservedProperty.FileCreatedAt => x => x.FileCreateDt,
								SearchableReservedProperty.FileModifiedAt => x => x.FileModifyDt,
								_ => null!
							};

							switch (filter.Operation)
							{
								case SearchOperation.Equals:
								{
									if (filterValue.HasValue)
									{
										set = context.ResourcesPool?.Where(x => filterValue == getValue(x.Value))
											.Select(x => x.Key).ToHashSet() ?? [];
									}

									break;
								}
								case SearchOperation.NotEquals:
								{
									if (filterValue.HasValue)
									{
										var ids = context.ResourcesPool?.Where(x => filterValue == getValue(x.Value))
											.Select(x => x.Key).ToHashSet();
										if (ids?.Any() == true)
										{
											set = context.AllResourceIds.Except(ids).ToHashSet();
										}
									}

									break;
								}
								case SearchOperation.GreaterThan:
								{
									if (filterValue.HasValue)
									{
										set = context.ResourcesPool?.Where(x => filterValue > getValue(x.Value))
											.Select(x => x.Key).ToHashSet() ?? [];
									}

									break;
								}
								case SearchOperation.LessThan:
								{
									if (filterValue.HasValue)
									{
										set = context.ResourcesPool?.Where(x => filterValue < getValue(x.Value))
											.Select(x => x.Key).ToHashSet() ?? [];
									}

									break;
								}
								case SearchOperation.GreaterThanOrEquals:
								{
									if (filterValue.HasValue)
									{
										set = context.ResourcesPool?.Where(x => filterValue >= getValue(x.Value))
											.Select(x => x.Key).ToHashSet() ?? [];
									}

									break;
								}
								case SearchOperation.LessThanOrEquals:
								{
									if (filterValue.HasValue)
									{
										set = context.ResourcesPool?.Where(x => filterValue <= getValue(x.Value))
											.Select(x => x.Key).ToHashSet() ?? [];
									}

									break;
								}
								case SearchOperation.IsNull:
								{
									var ids = context.ResourcesPool?.Select(x => x.Key).ToHashSet();
									if (ids?.Any() == true)
									{
										set = context.AllResourceIds.Except(ids).ToHashSet();
									}

									break;
								}
								case SearchOperation.IsNotNull:
								{
									set = context.ResourcesPool?.Select(x => x.Key).ToHashSet() ?? [];
									break;
								}
								default:
									throw new ArgumentOutOfRangeException();
							}

							break;
						}
						case SearchableReservedProperty.Category:
						case SearchableReservedProperty.MediaLibrary:
						{
							var getValue = property switch
							{
								SearchableReservedProperty.Category =>
									(Func<Abstractions.Models.Db.Resource, int>) (x => x.CategoryId),
								SearchableReservedProperty.MediaLibrary => x => x.MediaLibraryId,
								_ => null!
							};

							switch (filter.Operation)
							{
								case SearchOperation.Equals:
								case SearchOperation.NotEquals:
								{
									var filterValue = string.IsNullOrEmpty(filter.Value)
										? null
										: JsonConvert.DeserializeObject<int?>(filter.Value);
									switch (filter.Operation)
									{
										case SearchOperation.Equals:
										{
											if (filterValue.HasValue)
											{
												set = context.ResourcesPool
													?.Where(x => filterValue == getValue(x.Value))
													.Select(x => x.Key).ToHashSet() ?? [];
											}

											break;
										}
										case SearchOperation.NotEquals:
										{
											if (filterValue.HasValue)
											{
												var ids = context.ResourcesPool
													?.Where(x => filterValue == getValue(x.Value))
													.Select(x => x.Key).ToHashSet();
												if (ids?.Any() == true)
												{
													set = context.AllResourceIds.Except(ids).ToHashSet();
												}
											}

											break;
										}
										default:
											throw new ArgumentOutOfRangeException();
									}

									break;
								}
								case SearchOperation.In:
								case SearchOperation.NotIn:
								{
									var filterValue = string.IsNullOrEmpty(filter.Value)
										? null
										: JsonConvert.DeserializeObject<HashSet<int>>(filter.Value);
									switch (filter.Operation)
									{
										case SearchOperation.In:
										{
											if (filterValue?.Any() == true)
											{
												return context.ResourcesPool
													?.Where(x => filterValue.Contains(getValue(x.Value)))
													.Select(x => x.Key).ToHashSet();
											}

											break;
										}
										case SearchOperation.NotIn:
										{
											if (filterValue?.Any() == true)
											{
												return context.ResourcesPool
													?.Where(x => !filterValue.Contains(getValue(x.Value)))
													.Select(x => x.Key).ToHashSet();
											}

											break;
										}
										default:
											throw new ArgumentOutOfRangeException();
									}

									break;
								}
								default:
									throw new ArgumentOutOfRangeException();
							}

							break;
						}
						default:
							throw new ArgumentOutOfRangeException();
					}
				}
				else
				{
					await PrepareCustomProperties(context);
					var property = context.PropertiesDataPool![filter.PropertyId];
                    var propertyValues = await PrepareAndGetCustomPropertyValues(filter, context);

                    var descriptor = _propertyDescriptors.GetValueOrDefault(property.Type);
                    if (descriptor != null)
                    {
                        set = propertyValues?.Where(x => descriptor.IsMatch(x.Value, filter)).Select(x => x.Key)
                            .ToHashSet() ?? [];
                    }

                    //                switch (property.Type)
                    // {
                    // 	case CustomPropertyType.SingleLineText:
                    // 	case CustomPropertyType.MultilineText:
                    // 	case CustomPropertyType.Link:
                    // 	{
                    // 		var value = string.IsNullOrEmpty(filter.Value)
                    // 			? null
                    // 			: JsonConvert.DeserializeObject<string>(filter.Value);
                    //
                    // 		var propertyValues = await PrepareAndGetCustomPropertyValues<string>(filter, context);
                    //
                    // 		switch (filter.Operation)
                    // 		{
                    // 			case SearchOperation.Equals:
                    // 			{
                    // 				if (!string.IsNullOrEmpty(value))
                    // 				{
                    // 					set = propertyValues?.Where(x => value.Equals(x.Value))
                    // 						.Select(x => x.Key).ToHashSet() ?? [];
                    // 				}
                    //
                    // 				break;
                    // 			}
                    // 			case SearchOperation.NotEquals:
                    // 			{
                    // 				if (!string.IsNullOrEmpty(value))
                    // 				{
                    // 					var ids = propertyValues?.Where(x => value.Equals(x.Value))
                    // 						.Select(x => x.Key).ToHashSet();
                    // 					if (ids?.Any() == true)
                    // 					{
                    // 						set = context.AllResourceIds.Except(ids).ToHashSet();
                    // 					}
                    // 				}
                    //
                    // 				break;
                    // 			}
                    // 			case SearchOperation.Contains:
                    // 			{
                    // 				if (!string.IsNullOrEmpty(value))
                    // 				{
                    // 					set = propertyValues?.Where(x => value.Contains(x.Value!))
                    // 						.Select(x => x.Key).ToHashSet() ?? [];
                    // 				}
                    //
                    // 				break;
                    // 			}
                    // 			case SearchOperation.NotContains:
                    // 			{
                    // 				if (!string.IsNullOrEmpty(value))
                    // 				{
                    // 					var ids = propertyValues?.Where(x => value.Contains(x.Value))
                    // 						.Select(x => x.Key).ToHashSet();
                    // 					if (ids?.Any() == true)
                    // 					{
                    // 						set = context.AllResourceIds.Except(ids).ToHashSet();
                    // 					}
                    // 				}
                    //
                    // 				break;
                    // 			}
                    // 			case SearchOperation.StartsWith:
                    // 			{
                    // 				if (!string.IsNullOrEmpty(value))
                    // 				{
                    // 					set = propertyValues?.Where(x => value.StartsWith(x.Value!))
                    // 						.Select(x => x.Key).ToHashSet() ?? [];
                    // 				}
                    //
                    // 				break;
                    // 			}
                    // 			case SearchOperation.NotStartsWith:
                    // 			{
                    // 				if (!string.IsNullOrEmpty(value))
                    // 				{
                    // 					var ids = propertyValues?.Where(x => value.StartsWith(x.Value))
                    // 						.Select(x => x.Key).ToHashSet();
                    // 					if (ids?.Any() == true)
                    // 					{
                    // 						set = context.AllResourceIds.Except(ids).ToHashSet();
                    // 					}
                    // 				}
                    //
                    // 				break;
                    // 			}
                    // 			case SearchOperation.EndsWith:
                    // 			{
                    // 				if (!string.IsNullOrEmpty(value))
                    // 				{
                    // 					set = propertyValues?.Where(x => value.EndsWith(x.Value!))
                    // 						.Select(x => x.Key).ToHashSet() ?? [];
                    // 				}
                    //
                    // 				break;
                    // 			}
                    // 			case SearchOperation.NotEndsWith:
                    // 			{
                    // 				if (!string.IsNullOrEmpty(value))
                    // 				{
                    // 					var ids = propertyValues?.Where(x => value.EndsWith(x.Value))
                    // 						.Select(x => x.Key).ToHashSet();
                    // 					if (ids?.Any() == true)
                    // 					{
                    // 						set = context.AllResourceIds.Except(ids).ToHashSet();
                    // 					}
                    // 				}
                    //
                    // 				break;
                    // 			}
                    // 			case SearchOperation.IsNull:
                    // 			{
                    // 				var ids = propertyValues?.Select(x => x.Key).ToHashSet();
                    // 				if (ids?.Any() == true)
                    // 				{
                    // 					set = context.AllResourceIds.Except(ids).ToHashSet();
                    // 				}
                    //
                    // 				break;
                    // 			}
                    // 			case SearchOperation.IsNotNull:
                    // 			{
                    // 				set = propertyValues?.Select(x => x.Key).ToHashSet() ?? [];
                    // 				break;
                    // 			}
                    // 			case SearchOperation.Matches:
                    // 			{
                    // 				if (!string.IsNullOrEmpty(value))
                    // 				{
                    // 					var regex = new Regex(value);
                    // 					set = propertyValues?.Where(x => regex.IsMatch(x.Value!))
                    // 						.Select(x => x.Key).ToHashSet() ?? [];
                    // 				}
                    //
                    // 				break;
                    // 			}
                    // 			case SearchOperation.NotMatches:
                    // 			{
                    // 				if (!string.IsNullOrEmpty(value))
                    // 				{
                    // 					var regex = new Regex(value);
                    // 					var ids = propertyValues?.Where(x => regex.IsMatch(x.Value!))
                    // 						.Select(x => x.Key).ToHashSet();
                    // 					if (ids?.Any() == true)
                    // 					{
                    // 						set = context.AllResourceIds.Except(ids).ToHashSet();
                    // 					}
                    // 				}
                    //
                    // 				break;
                    // 			}
                    // 			default:
                    // 				throw new ArgumentOutOfRangeException();
                    // 		}
                    //
                    // 		break;
                    // 	}
                    // 	case CustomPropertyType.SingleTextChoice:
                    // 	{
                    // 		var propertyValues = await PrepareAndGetCustomPropertyValues<string>(filter, context);
                    //
                    // 		switch (filter.Operation)
                    // 		{
                    // 			case SearchOperation.Equals:
                    // 			case SearchOperation.NotEquals:
                    // 			case SearchOperation.IsNull:
                    // 			case SearchOperation.IsNotNull:
                    // 			{
                    // 				var value = string.IsNullOrEmpty(filter.Value)
                    // 					? null
                    // 					: JsonConvert.DeserializeObject<string>(filter.Value);
                    // 				switch (filter.Operation)
                    // 				{
                    // 					case SearchOperation.Equals:
                    // 					{
                    // 						if (!string.IsNullOrEmpty(value))
                    // 						{
                    // 							set = propertyValues?.Where(x => value.Equals(x.Value))
                    // 								.Select(x => x.Key)
                    // 								.ToHashSet();
                    // 						}
                    //
                    // 						break;
                    // 					}
                    // 					case SearchOperation.NotEquals:
                    // 					{
                    // 						if (!string.IsNullOrEmpty(value))
                    // 						{
                    // 							var ids = propertyValues?.Where(x => value.Equals(x.Value))
                    // 								.Select(x => x.Key)
                    // 								.ToHashSet();
                    // 							if (ids?.Any() == true)
                    // 							{
                    // 								set = context.AllResourceIds.Except(ids).ToHashSet();
                    // 							}
                    // 						}
                    //
                    // 						break;
                    // 					}
                    // 					case SearchOperation.IsNull:
                    // 					{
                    // 						var ids = propertyValues?.Select(x => x.Key).ToHashSet();
                    // 						if (ids?.Any() == true)
                    // 						{
                    // 							set = context.AllResourceIds.Except(ids).ToHashSet();
                    // 						}
                    //
                    // 						break;
                    // 					}
                    // 					case SearchOperation.IsNotNull:
                    // 					{
                    // 						set = propertyValues?.Select(x => x.Key).ToHashSet() ?? [];
                    // 						break;
                    // 					}
                    // 					default:
                    // 						throw new ArgumentOutOfRangeException();
                    // 				}
                    //
                    // 				break;
                    // 			}
                    // 			case SearchOperation.In:
                    // 			case SearchOperation.NotIn:
                    // 			{
                    // 				var value = string.IsNullOrEmpty(filter.Value)
                    // 					? null
                    // 					: JsonConvert.DeserializeObject<HashSet<string>>(filter.Value);
                    // 				if (value?.Any() == true)
                    // 				{
                    // 					switch (filter.Operation)
                    // 					{
                    // 						case SearchOperation.In:
                    // 						{
                    // 							set = propertyValues?.Where(x => value.Contains(x.Value))
                    // 								.Select(x => x.Key)
                    // 								.ToHashSet() ?? [];
                    // 							break;
                    // 						}
                    // 						case SearchOperation.NotIn:
                    // 						{
                    // 							var ids = propertyValues?.Where(x => value.Contains(x.Value))
                    // 								.Select(x => x.Key)
                    // 								.ToHashSet();
                    // 							if (ids?.Any() == true)
                    // 							{
                    // 								set = context.AllResourceIds.Except(ids).ToHashSet();
                    // 							}
                    //
                    // 							break;
                    // 						}
                    // 						default:
                    // 							throw new ArgumentOutOfRangeException();
                    // 					}
                    // 				}
                    //
                    // 				break;
                    // 			}
                    // 			default:
                    // 				throw new ArgumentOutOfRangeException();
                    // 		}
                    //
                    // 		break;
                    // 	}
                    // 	case CustomPropertyType.MultipleTextChoice:
                    // 	{
                    // 		var value = string.IsNullOrEmpty(filter.Value)
                    // 			? null
                    // 			: JsonConvert.DeserializeObject<string>(filter.Value);
                    // 		var propertyValues =
                    // 			await PrepareAndGetCustomPropertyValues<HashSet<string>>(filter, context);
                    // 		switch (filter.Operation)
                    // 		{
                    // 			case SearchOperation.Contains:
                    // 			{
                    // 				if (value?.Any() == true)
                    // 				{
                    // 					set = propertyValues?.Where(x => x.Value.Contains(value))
                    // 						.Select(x => x.Key).ToHashSet() ?? [];
                    // 				}
                    //
                    // 				break;
                    // 			}
                    // 			case SearchOperation.NotContains:
                    // 			{
                    // 				if (value?.Any() == true)
                    // 				{
                    // 					var ids = propertyValues?.Where(x => x.Value.Contains(value))
                    // 						.Select(x => x.Key).ToHashSet();
                    // 					if (ids?.Any() == true)
                    // 					{
                    // 						set = context.AllResourceIds.Except(ids).ToHashSet();
                    // 					}
                    // 				}
                    //
                    // 				break;
                    // 			}
                    // 			case SearchOperation.IsNull:
                    // 			{
                    // 				var ids = propertyValues?.Where(x => x.Value.Any()).Select(x => x.Key).ToHashSet();
                    // 				if (ids?.Any() == true)
                    // 				{
                    // 					set = context.AllResourceIds.Except(ids).ToHashSet();
                    // 				}
                    //
                    // 				break;
                    // 			}
                    // 			case SearchOperation.IsNotNull:
                    // 			{
                    // 				set = propertyValues?.Where(x => x.Value.Any()).Select(x => x.Key)
                    // 					.ToHashSet() ?? [];
                    // 				break;
                    // 			}
                    // 			default:
                    // 				throw new ArgumentOutOfRangeException();
                    // 		}
                    //
                    // 		break;
                    // 	}
                    // 	case CustomPropertyType.Number:
                    // 	case CustomPropertyType.Percentage:
                    // 	case CustomPropertyType.Rating:
                    // 	{
                    // 		var value = string.IsNullOrEmpty(filter.Value)
                    // 			? null
                    // 			: JsonConvert.DeserializeObject<decimal?>(filter.Value);
                    // 		var propertyValues = await PrepareAndGetCustomPropertyValues<decimal>(filter, context);
                    // 		switch (filter.Operation)
                    // 		{
                    // 			case SearchOperation.Equals:
                    // 			{
                    // 				if (value.HasValue)
                    // 				{
                    // 					set = propertyValues?.Where(x => value == x.Value)
                    // 						.Select(x => x.Key).ToHashSet() ?? [];
                    // 				}
                    //
                    // 				break;
                    // 			}
                    // 			case SearchOperation.NotEquals:
                    // 			{
                    // 				if (value.HasValue)
                    // 				{
                    // 					var ids = propertyValues?.Where(x => value == x.Value)
                    // 						.Select(x => x.Key).ToHashSet();
                    // 					if (ids?.Any() == true)
                    // 					{
                    // 						set = context.AllResourceIds.Except(ids).ToHashSet();
                    // 					}
                    // 				}
                    //
                    // 				break;
                    // 			}
                    // 			case SearchOperation.GreaterThan:
                    // 			{
                    // 				if (value.HasValue)
                    // 				{
                    // 					set = propertyValues?.Where(x => value > x.Value)
                    // 						.Select(x => x.Key).ToHashSet() ?? [];
                    // 				}
                    //
                    // 				break;
                    // 			}
                    // 			case SearchOperation.LessThan:
                    // 			{
                    // 				if (value.HasValue)
                    // 				{
                    // 					set = propertyValues?.Where(x => value < x.Value)
                    // 						.Select(x => x.Key).ToHashSet() ?? [];
                    // 				}
                    //
                    // 				break;
                    // 			}
                    // 			case SearchOperation.GreaterThanOrEquals:
                    // 			{
                    // 				if (value.HasValue)
                    // 				{
                    // 					set = propertyValues?.Where(x => value >= x.Value)
                    // 						.Select(x => x.Key).ToHashSet() ?? [];
                    // 				}
                    //
                    // 				break;
                    // 			}
                    // 			case SearchOperation.LessThanOrEquals:
                    // 			{
                    // 				if (value.HasValue)
                    // 				{
                    // 					set = propertyValues?.Where(x => value <= x.Value)
                    // 						.Select(x => x.Key).ToHashSet() ?? [];
                    // 				}
                    //
                    // 				break;
                    // 			}
                    // 			case SearchOperation.IsNull:
                    // 			{
                    // 				var ids = propertyValues?.Select(x => x.Key).ToHashSet();
                    // 				if (ids?.Any() == true)
                    // 				{
                    // 					set = context.AllResourceIds.Except(ids).ToHashSet();
                    // 				}
                    //
                    // 				break;
                    // 			}
                    // 			case SearchOperation.IsNotNull:
                    // 			{
                    // 				set = propertyValues?.Select(x => x.Key).ToHashSet() ?? [];
                    // 				break;
                    // 			}
                    // 			default:
                    // 				throw new ArgumentOutOfRangeException();
                    // 		}
                    //
                    // 		break;
                    // 	}
                    // 	case CustomPropertyType.Boolean:
                    // 	{
                    // 		var value = string.IsNullOrEmpty(filter.Value)
                    // 			? null
                    // 			: JsonConvert.DeserializeObject<bool?>(filter.Value);
                    // 		var propertyValues = await PrepareAndGetCustomPropertyValues<bool>(filter, context);
                    // 		switch (filter.Operation)
                    // 		{
                    // 			case SearchOperation.Equals:
                    // 			{
                    // 				if (value.HasValue)
                    // 				{
                    // 					set = propertyValues?.Where(x => value == x.Value)
                    // 						.Select(x => x.Key).ToHashSet() ?? [];
                    // 				}
                    //
                    // 				break;
                    // 			}
                    // 			case SearchOperation.NotEquals:
                    // 			{
                    // 				if (value.HasValue)
                    // 				{
                    // 					var ids = propertyValues?.Where(x => value == x.Value)
                    // 						.Select(x => x.Key).ToHashSet();
                    // 					if (ids?.Any() == true)
                    // 					{
                    // 						set = context.AllResourceIds.Except(ids).ToHashSet();
                    // 					}
                    // 				}
                    //
                    // 				break;
                    // 			}
                    // 			case SearchOperation.IsNull:
                    // 			{
                    // 				var ids = propertyValues?.Select(x => x.Key).ToHashSet();
                    // 				if (ids?.Any() == true)
                    // 				{
                    // 					set = context.AllResourceIds.Except(ids).ToHashSet();
                    // 				}
                    //
                    // 				break;
                    // 			}
                    // 			case SearchOperation.IsNotNull:
                    // 			{
                    // 				set = propertyValues?.Select(x => x.Key).ToHashSet() ?? [];
                    // 				break;
                    // 			}
                    // 			default:
                    // 				throw new ArgumentOutOfRangeException();
                    // 		}
                    //
                    // 		break;
                    // 	}
                    // 	case CustomPropertyType.Attachment:
                    // 	{
                    // 		var propertyValues = await PrepareAndGetCustomPropertyValues<HashSet<int>>(filter, context);
                    // 		switch (filter.Operation)
                    // 		{
                    // 			case SearchOperation.IsNull:
                    // 			{
                    // 				var ids = propertyValues?.Select(x => x.Key).ToHashSet();
                    // 				if (ids?.Any() == true)
                    // 				{
                    // 					set = context.AllResourceIds.Except(ids).ToHashSet();
                    // 				}
                    //
                    // 				break;
                    // 			}
                    // 			case SearchOperation.IsNotNull:
                    // 			{
                    // 				set = propertyValues?.Select(x => x.Key).ToHashSet() ?? [];
                    // 				break;
                    // 			}
                    // 			default:
                    // 				throw new ArgumentOutOfRangeException();
                    // 		}
                    //
                    // 		break;
                    // 	}
                    // 	case CustomPropertyType.Date:
                    // 	case CustomPropertyType.DateTime:
                    // 	{
                    // 		var value = string.IsNullOrEmpty(filter.Value)
                    // 			? null
                    // 			: JsonConvert.DeserializeObject<DateTime?>(filter.Value);
                    // 		var propertyValues = await PrepareAndGetCustomPropertyValues(filter, context);
                    //
                    //                        ICustomPropertyDescriptor descriptor = null!;
                    //
                    //                        var validIds = propertyValues?.Where(x => descriptor.IsMatch(x.Value, filter))
                    //                            .Select(x => x.Key).ToHashSet() ?? [];
                    //
                    //
                    //
                    //                            switch (filter.Operation)
                    // 		{
                    // 			case SearchOperation.Equals:
                    // 			{
                    // 				if (value.HasValue)
                    // 				{
                    // 					set = propertyValues?.Where(x => descriptor.IsMatch(x.Value, filter))
                    // 						.Select(x => x.Key).ToHashSet() ?? [];
                    // 				}
                    //
                    // 				break;
                    // 			}
                    // 			case SearchOperation.NotEquals:
                    // 			{
                    // 				if (value.HasValue)
                    // 				{
                    // 					var ids = propertyValues?.Where(x => value == x.Value)
                    // 						.Select(x => x.Key).ToHashSet();
                    // 					if (ids?.Any() == true)
                    // 					{
                    // 						set = context.AllResourceIds.Except(ids).ToHashSet();
                    // 					}
                    // 				}
                    //
                    // 				break;
                    // 			}
                    // 			case SearchOperation.GreaterThan:
                    // 			{
                    // 				if (value.HasValue)
                    // 				{
                    // 					set = propertyValues?.Where(x => value > x.Value)
                    // 						.Select(x => x.Key).ToHashSet() ?? [];
                    // 				}
                    //
                    // 				break;
                    // 			}
                    // 			case SearchOperation.LessThan:
                    // 			{
                    // 				if (value.HasValue)
                    // 				{
                    // 					set = propertyValues?.Where(x => value < x.Value)
                    // 						.Select(x => x.Key).ToHashSet() ?? [];
                    // 				}
                    //
                    // 				break;
                    // 			}
                    // 			case SearchOperation.GreaterThanOrEquals:
                    // 			{
                    // 				if (value.HasValue)
                    // 				{
                    // 					set = propertyValues?.Where(x => value >= x.Value)
                    // 						.Select(x => x.Key).ToHashSet() ?? [];
                    // 				}
                    //
                    // 				break;
                    // 			}
                    // 			case SearchOperation.LessThanOrEquals:
                    // 			{
                    // 				if (value.HasValue)
                    // 				{
                    // 					set = propertyValues?.Where(x => value <= x.Value)
                    // 						.Select(x => x.Key).ToHashSet() ?? [];
                    // 				}
                    //
                    // 				break;
                    // 			}
                    // 			case SearchOperation.IsNull:
                    // 			{
                    // 				var ids = propertyValues?.Select(x => x.Key).ToHashSet();
                    // 				if (ids?.Any() == true)
                    // 				{
                    // 					set = context.AllResourceIds.Except(ids).ToHashSet();
                    // 				}
                    //
                    // 				break;
                    // 			}
                    // 			case SearchOperation.IsNotNull:
                    // 			{
                    // 				set = propertyValues?.Select(x => x.Key).ToHashSet() ?? [];
                    // 				break;
                    // 			}
                    // 			default:
                    // 				throw new ArgumentOutOfRangeException();
                    // 		}
                    //
                    // 		break;
                    // 	}
                    // 	case CustomPropertyType.Time:
                    // 	{
                    // 		var value = string.IsNullOrEmpty(filter.Value)
                    // 			? null
                    // 			: JsonConvert.DeserializeObject<TimeSpan?>(filter.Value);
                    // 		var propertyValues = await PrepareAndGetCustomPropertyValues<TimeSpan>(filter, context);
                    // 		switch (filter.Operation)
                    // 		{
                    // 			case SearchOperation.Equals:
                    // 			{
                    // 				if (value.HasValue)
                    // 				{
                    // 					set = propertyValues?.Where(x => value == x.Value)
                    // 						.Select(x => x.Key).ToHashSet() ?? [];
                    // 				}
                    //
                    // 				break;
                    // 			}
                    // 			case SearchOperation.NotEquals:
                    // 			{
                    // 				if (value.HasValue)
                    // 				{
                    // 					var ids = propertyValues?.Where(x => value == x.Value)
                    // 						.Select(x => x.Key).ToHashSet();
                    // 					if (ids?.Any() == true)
                    // 					{
                    // 						set = context.AllResourceIds.Except(ids).ToHashSet();
                    // 					}
                    // 				}
                    //
                    // 				break;
                    // 			}
                    // 			case SearchOperation.GreaterThan:
                    // 			{
                    // 				if (value.HasValue)
                    // 				{
                    // 					set = propertyValues?.Where(x => value > x.Value)
                    // 						.Select(x => x.Key).ToHashSet() ?? [];
                    // 				}
                    //
                    // 				break;
                    // 			}
                    // 			case SearchOperation.LessThan:
                    // 			{
                    // 				if (value.HasValue)
                    // 				{
                    // 					set = propertyValues?.Where(x => value < x.Value)
                    // 						.Select(x => x.Key).ToHashSet() ?? [];
                    // 				}
                    //
                    // 				break;
                    // 			}
                    // 			case SearchOperation.GreaterThanOrEquals:
                    // 			{
                    // 				if (value.HasValue)
                    // 				{
                    // 					set = propertyValues?.Where(x => value >= x.Value)
                    // 						.Select(x => x.Key).ToHashSet() ?? [];
                    // 				}
                    //
                    // 				break;
                    // 			}
                    // 			case SearchOperation.LessThanOrEquals:
                    // 			{
                    // 				if (value.HasValue)
                    // 				{
                    // 					set = propertyValues?.Where(x => value <= x.Value)
                    // 						.Select(x => x.Key).ToHashSet() ?? [];
                    // 				}
                    //
                    // 				break;
                    // 			}
                    // 			case SearchOperation.IsNull:
                    // 			{
                    // 				var ids = propertyValues?.Select(x => x.Key).ToHashSet();
                    // 				if (ids?.Any() == true)
                    // 				{
                    // 					set = context.AllResourceIds.Except(ids).ToHashSet();
                    // 				}
                    //
                    // 				break;
                    // 			}
                    // 			case SearchOperation.IsNotNull:
                    // 			{
                    // 				set = propertyValues?.Select(x => x.Key).ToHashSet() ?? [];
                    // 				break;
                    // 			}
                    // 			default:
                    // 				throw new ArgumentOutOfRangeException();
                    // 		}
                    //
                    // 		break;
                    // 	}
                    // 	case CustomPropertyType.Formula:
                    // 	case CustomPropertyType.MultipleTextTree:
                    // 		break;
                    // 	default:
                    // 		throw new ArgumentOutOfRangeException();
                    // }
                }
            }

			return set;
		}
	}
}