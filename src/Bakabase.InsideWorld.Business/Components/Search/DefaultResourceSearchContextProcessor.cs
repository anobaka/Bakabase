using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Extensions;
using Bakabase.InsideWorld.Models.Models.Aos;
using Bakabase.InsideWorld.Models.Models.Dtos.CustomProperty.Abstractions;
using Newtonsoft.Json;

namespace Bakabase.InsideWorld.Business.Components.Search
{
	public class DefaultResourceSearchContextProcessor : IResourceSearchContextProcessor
	{
		public Task Search(ResourceSearchFilter filter, ResourceSearchContext context)
		{
			if (filter.PropertyId != 0 && filter.Operation != 0)
			{
				#region Aliases

				if (isReservedProperty && ((SearchableReservedProperty)propertyId).ToResourceProperty().AppliedAliases() ||
				    !isReservedProperty)
				{
					context.Aliases ??= await _aliasService.GetFullMap();
				}

				#endregion

				if (filter.IsReservedProperty)
				{
					var property = (SearchableReservedProperty) filter.PropertyId;
					switch (property)
					{
						case SearchableReservedProperty.Name:
						case SearchableReservedProperty.FileName:
						case SearchableReservedProperty.DirectoryName:
						case SearchableReservedProperty.DirectoryPath:
						case SearchableReservedProperty.CreatedAt:
						case SearchableReservedProperty.ModifiedAt:
						case SearchableReservedProperty.FileCreatedAt:
						case SearchableReservedProperty.FileModifiedAt:
						case SearchableReservedProperty.Category:
						case SearchableReservedProperty.MediaLibrary:
						{
							var missingResourceIds = context.LimitedResourceIds == null ? null :
								context.ResourcesPool == null ? context.LimitedResourceIds :
								context.LimitedResourceIds.Except(context.ResourcesPool.Keys).ToHashSet();
							if (missingResourceIds == null || missingResourceIds.Any())
							{
								var resources = await GetAllEntities(missingResourceIds == null
									? null
									: r => missingResourceIds.Contains(r.Id));
								if (context.ResourcesPool == null)
								{
									context.ResourcesPool = resources.ToDictionary(r => r.Id, r => r);
								}
								else
								{
									foreach (var r in resources)
									{
										context.ResourcesPool[r.Id] = r;
									}
								}
							}

							break;
						}
						case SearchableReservedProperty.Favorites:
						{
							switch (filter.Operation)
							{
								case SearchOperation.Equals:
								case SearchOperation.NotEquals:
									// prepare one data
									break;
								case SearchOperation.IsNull:
								case SearchOperation.IsNotNull:
									// prepare all data
									break;
								case SearchOperation.In:
								case SearchOperation.NotIn:
									// prepare selected data
									break;
								default:
									throw new ArgumentOutOfRangeException();
							}

							break;
						}
						case SearchableReservedProperty.Tag:
						{
							switch (filter.Operation)
							{
								case SearchOperation.Equals:
								case SearchOperation.NotEquals:
									// prepare one data
									break;
								case SearchOperation.IsNull:
								case SearchOperation.IsNotNull:
									// prepare all data
									break;
								case SearchOperation.In:
								case SearchOperation.NotIn:
									// prepare selected data
									break;
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
					CustomPropertyDto property;
					switch (property.Type)
					{
						case CustomPropertyType.SingleLineText:
						case CustomPropertyType.MultilineText:
						case CustomPropertyType.Link:
						{
							var value = string.IsNullOrEmpty(filter.Value)
								? null
								: JsonConvert.DeserializeObject<string>(filter.Value);

							if (context.CustomPropertyDataPool?.TryGetValue(filter.PropertyId,
								    out var propertyValues) != true)
							{
								propertyValues =
									(await _customPropertyValueService.GetAll(x => x.PropertyId == filter.PropertyId))
									.Select(x => (ResourceId: x.ResourceId, Value: string.IsNullOrEmpty(x.Value)
										? null
										: (object?) JsonConvert.DeserializeObject<string>(x.Value))).ToList();
								context.CustomPropertyDataPool ??= new();
								context.CustomPropertyDataPool[filter.PropertyId] = propertyValues;
							}

							switch (filter.Operation)
							{
								case SearchOperation.Equals:
								{
									if (!string.IsNullOrEmpty(value))
									{
										var ids = propertyValues!.Where(x => value.Equals(x.Value))
											.Select(x => x.ResourceId).ToHashSet();
										(context.LimitedResourceIds ??= []).IntersectWith(ids);
									}

									break;
								}
								case SearchOperation.NotEquals:
								{
									if (!string.IsNullOrEmpty(value))
									{
										var ids = propertyValues!.Where(x => value.Equals(x.Value))
											.Select(x => x.ResourceId).ToHashSet();
										(context.ExcludedResourceIds ??= []).IntersectWith(ids);
									}

									break;
								}
								case SearchOperation.Contains:
								{
									if (!string.IsNullOrEmpty(value))
									{
										var ids = propertyValues!.Where(x => value.Contains((x.Value as string)!))
											.Select(x => x.ResourceId).ToHashSet();
										(context.LimitedResourceIds ??= []).IntersectWith(ids);
									}

									break;
								}
								case SearchOperation.NotContains:
									break;
								case SearchOperation.StartsWith:
								{
									if (!string.IsNullOrEmpty(value))
									{
										var ids = propertyValues!.Where(x => value.StartsWith((x.Value as string)!))
											.Select(x => x.ResourceId).ToHashSet();
										(context.LimitedResourceIds ??= []).IntersectWith(ids);
									}

									break;
								}
								case SearchOperation.NotStartsWith:
									break;
								case SearchOperation.EndsWith:
								{
									if (!string.IsNullOrEmpty(value))
									{
										var ids = propertyValues!.Where(x => value.EndsWith((x.Value as string)!))
											.Select(x => x.ResourceId).ToHashSet();
										(context.LimitedResourceIds ??= []).IntersectWith(ids);
									}

									break;
								}
								case SearchOperation.NotEndsWith:
									break;
								case SearchOperation.IsNull:
								{
									var ids = propertyValues!.Select(x => x.ResourceId).ToHashSet();
									(context.ExcludedResourceIds ??= []).IntersectWith(ids);
									break;
								}
								case SearchOperation.IsNotNull:
								{
									var ids = propertyValues!.Select(x => x.ResourceId).ToHashSet();
									(context.LimitedResourceIds ??= []).IntersectWith(ids);
									break;
								}
								case SearchOperation.Matches:
								{
									if (!string.IsNullOrEmpty(value))
									{
										var regex = new Regex(value);
										var ids = propertyValues!.Where(x => regex.IsMatch((x.Value as string)!))
											.Select(x => x.ResourceId).ToHashSet();
										(context.LimitedResourceIds ??= []).IntersectWith(ids);
									}

									break;
								}
								case SearchOperation.NotMatches:
								{
									if (!string.IsNullOrEmpty(value))
									{
										var regex = new Regex(value);
										var ids = propertyValues!.Where(x => !regex.IsMatch((x.Value as string)!))
											.Select(x => x.ResourceId).ToHashSet();
										(context.ExcludedResourceIds ??= []).IntersectWith(ids);
									}

									break;
								}
								default:
									throw new ArgumentOutOfRangeException();
							}

							break;
						}
						case CustomPropertyType.SingleChoice:
						{
							switch (filter.Operation)
							{
								case SearchOperation.Equals:
									break;
								case SearchOperation.NotEquals:
									break;
								case SearchOperation.IsNull:
									break;
								case SearchOperation.IsNotNull:
									break;
								case SearchOperation.In:
									break;
								case SearchOperation.NotIn:
									break;
								default:
									throw new ArgumentOutOfRangeException();
							}

							break;
						}
						case CustomPropertyType.MultipleChoice:
						{
							switch (filter.Operation)
							{
								case SearchOperation.Contains:
									break;
								case SearchOperation.NotContains:
									break;
								case SearchOperation.IsNull:
									break;
								case SearchOperation.IsNotNull:
									break;
								default:
									throw new ArgumentOutOfRangeException();
							}

							break;
						}
						case CustomPropertyType.Number:
						case CustomPropertyType.Percentage:
						case CustomPropertyType.Rating:

						{
							switch (filter.Operation)
							{
								case SearchOperation.Equals:
									break;
								case SearchOperation.NotEquals:
									break;
								case SearchOperation.GreaterThan:
									break;
								case SearchOperation.LessThan:
									break;
								case SearchOperation.GreaterThanOrEquals:
									break;
								case SearchOperation.LessThanOrEquals:
									break;
								case SearchOperation.IsNull:
									break;
								case SearchOperation.IsNotNull:
									break;
								default:
									throw new ArgumentOutOfRangeException();
							}

							break;
						}
						case CustomPropertyType.Boolean:
						{
							switch (filter.Operation)
							{
								case SearchOperation.Equals:
									break;
								case SearchOperation.NotEquals:
									break;
								case SearchOperation.IsNull:
									break;
								case SearchOperation.IsNotNull:
									break;
								default:
									throw new ArgumentOutOfRangeException();
							}

							break;
						}
						case CustomPropertyType.Attachment:
						{
							switch (filter.Operation)
							{
								case SearchOperation.IsNull:
									break;
								case SearchOperation.IsNotNull:
									break;
								default:
									throw new ArgumentOutOfRangeException();
							}

							break;
						}
						default:
							throw new ArgumentOutOfRangeException();
					}
				}
			}
		}
	}
}