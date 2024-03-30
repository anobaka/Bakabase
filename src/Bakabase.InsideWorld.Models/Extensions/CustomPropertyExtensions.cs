using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Aos;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bakabase.InsideWorld.Models.Models.Dtos.CustomProperty;
using Bakabase.InsideWorld.Models.Models.Dtos.CustomProperty.Abstractions;
using Bakabase.InsideWorld.Models.Models.Dtos.CustomProperty.Properties.Choice;
using Bakabase.InsideWorld.Models.Models.Entities;
using Newtonsoft.Json;

namespace Bakabase.InsideWorld.Models.Extensions
{
	public static class CustomPropertyExtensions
	{
		public static Dictionary<string, List<CustomResourceProperty>> Merge(
			this Dictionary<string, List<CustomResourceProperty>> a, Dictionary<string, List<CustomResourceProperty>> b)
		{
			var result = a.ToDictionary(x => x.Key, x => x.Value.Select(y => y with { }).ToList());

			foreach (var (key, listB) in b)
			{
				if (result.TryGetValue(key, out var listA))
				{
					var restB = listB.ToHashSet();
					foreach (var cpa in listA)
					{
						var cpb = listB.FirstOrDefault(x => x.Index == cpa.Index);
						if (cpb != null)
						{
							restB.Remove(cpb);
							cpa.Value = cpb.Value;
						}
					}

					listA.AddRange(restB.Select(x => x with { }));
				}
				else
				{
					result[key] = listB.Select(x => x with { }).ToList();
				}
			}

			return result;
		}

		public static CustomResourceProperty Clone(this CustomResourceProperty customResourceProperty)
		{
			return customResourceProperty with { };
		}

		public static ResourceDiff? Compare(this List<CustomResourceProperty>? a, List<CustomResourceProperty>? b)
		{
			if (a == null && b == null)
			{
				return null;
			}

			if (a == null)
			{
				return ResourceDiff.Added(ResourceDiffProperty.CustomProperty, b);
			}

			if (b == null)
			{
				return ResourceDiff.Removed(ResourceDiffProperty.CustomProperty, a);
			}

			var aMapByKey = a.GroupBy(x => x.Key).ToDictionary(x => x.Key, x => x.OrderBy(c => c.Index).ToList());
			var bMapByKey = b.GroupBy(x => x.Key).ToDictionary(x => x.Key, x => x.OrderBy(c => c.Index).ToList());

			var allKeys = aMapByKey.Keys.Union(bMapByKey.Keys).ToHashSet();

			var diffsByKey = new List<ResourceDiff>();

			foreach (var key in allKeys)
			{
				var aValuesByKey = aMapByKey.GetValueOrDefault(key);
				var bValuesByKey = bMapByKey.GetValueOrDefault(key);

				ResourceDiff? keyDiff = null;

				// At least one of the values is not null
				if (aValuesByKey == null)
				{
					keyDiff = ResourceDiff.Added(ResourceDiffProperty.CustomProperty, bValuesByKey);
				}
				else
				{
					if (bValuesByKey == null)
					{
						keyDiff = ResourceDiff.Removed(ResourceDiffProperty.CustomProperty, aValuesByKey);
					}
					else
					{
						// check elements
						var maxCount = Math.Max(aValuesByKey.Count, bValuesByKey.Count);
						var diffsByIndex = new List<ResourceDiff>();
						for (var i = 0; i < maxCount; i++)
						{
							var aIndexedValue = aValuesByKey.ElementAtOrDefault(i);
							var bIndexedValue = bValuesByKey.ElementAtOrDefault(i);

							ResourceDiff? indexedValueDiff = null;

							// At least one of the values is not null
							if (aIndexedValue == null)
							{
								indexedValueDiff =
									ResourceDiff.Added(ResourceDiffProperty.CustomProperty, bIndexedValue);
							}
							else
							{
								if (bIndexedValue == null)
								{
									indexedValueDiff =
										ResourceDiff.Removed(ResourceDiffProperty.CustomProperty, aIndexedValue);
								}
								else
								{
									if (aIndexedValue.Value != bIndexedValue.Value)
									{
										indexedValueDiff = new ResourceDiff
										{
											CurrentValue = aIndexedValue,
											NewValue = bIndexedValue,
											Key = key,
											Property = ResourceDiffProperty.CustomProperty,
											Type = ResourceDiffType.Modified,
											SubDiffs = new List<ResourceDiff>
											{
												new ResourceDiff
												{
													CurrentValue = aIndexedValue?.Value,
													NewValue = bIndexedValue?.Value,
													Type = ResourceDiffType.Modified,
													Property = ResourceDiffProperty.CustomProperty,
													Key = i.ToString()
												}
											}
										};
									}
								}
							}


							if (indexedValueDiff != null)
							{
								diffsByIndex.Add(indexedValueDiff);
							}
						}

						if (diffsByIndex.Any())
						{
							keyDiff ??= new ResourceDiff
							{
								Key = key,
								Property = ResourceDiffProperty.CustomProperty,
								Type = ResourceDiffType.Modified,
								SubDiffs = diffsByIndex,
								CurrentValue = aValuesByKey,
								NewValue = bValuesByKey
							};
						}
					}
				}

				if (keyDiff != null)
				{
					diffsByKey.Add(keyDiff);
				}
			}

			return diffsByKey.Any()
				? new ResourceDiff
				{
					CurrentValue = a,
					NewValue = b,
					Key = nameof(ResourceDto.CustomProperties),
					Property = ResourceDiffProperty.CustomProperty,
					SubDiffs = diffsByKey,
					Type = ResourceDiffType.Modified
				}
				: null;
		}

		public static CustomProperty? ToEntity<T>(this CustomPropertyDto<T>? dto)
		{
			if (dto == null)
			{
				return null;
			}

			return new CustomProperty
			{
				CreatedAt = dto.CreatedAt,
				Name = dto.Name,
				Id = dto.Id,
				Type = dto.Type,
				Options = dto.Options == null ? null : JsonConvert.SerializeObject(dto.Options)
			};
		}

		public static Dictionary<CustomPropertyType, ICustomPropertyDescriptor> Descriptors = new()
		{
			{
				CustomPropertyType.SingleChoice, new SingleChoicePropertyDescriptor()
			}
		};

		public static CustomPropertyDto? ToDto(this CustomProperty? entity)
		{
			if (entity == null)
			{
				return null;
			}

			return Descriptors[entity.Type].BuildPropertyDto(entity);
		}
	}
}