using System;
using System.Collections.Generic;
using System.Linq;
using Aliyun.Api.LogService.Infrastructure.Serialization.Protobuf;
using Bakabase.InsideWorld.Models.Models.Aos;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bakabase.InsideWorld.Models.Models.Entities.Implicit;
using Bootstrap.Extensions;
using Microsoft.AspNetCore.JsonPatch.Converters;
using Newtonsoft.Json;
using Serilog.Core;

namespace Bakabase.InsideWorld.Models.Extensions
{
	public static class MediaLibraryExtensions
	{
		#region Serialization

		public static string? Serialize(this IEnumerable<PathConfigurationDto>? pcs)
		{
			if (pcs != null)
			{
				try
				{
					return JsonConvert.SerializeObject(pcs.Select(pc => pc.ToEntity()));
				}
				catch
				{
					// ignored
				}
			}

			return null;
		}

		public static List<PathConfigurationDto>? DeserializeAsPathConfigurationDtoList(this string? json)
		{
			if (!string.IsNullOrEmpty(json))
			{
				try
				{
					return JsonConvert.DeserializeObject<List<PathConfiguration>>(json)!.Select(p => p.ToDto()!)
						.ToList();
				}
				catch (Exception)
				{
					// ignored
				}
			}

			return null;
		}

		#endregion

		public static PathConfigurationDto? ToDto(this PathConfiguration? pc)
		{
			if (pc == null)
			{
				return null;
			}

			return new PathConfigurationDto
			{
				FixedTagIds = pc.FixedTagIds,
				Path = pc.Path,
				RpmValues = pc.RpmValues
			};
		}

		public static PathConfiguration? ToEntity(this PathConfigurationDto? dto)
		{
			if (dto == null)
			{
				return null;
			}

			return new PathConfiguration
			{
				FixedTagIds = dto.FixedTagIds,
				Path = dto.Path,
				RpmValues = dto.RpmValues
			};
		}

		public static MediaLibraryDto? ToDto(this MediaLibrary? ml)
		{
			if (ml == null)
			{
				return null;
			}

			var dto = new MediaLibraryDto
			{
				CategoryId = ml.CategoryId,
				Name = ml.Name,
				Order = ml.Order,
				Id = ml.Id,
				ResourceCount = ml.ResourceCount,
				PathConfigurations = ml.PathConfigurationsJson.DeserializeAsPathConfigurationDtoList()
			};

			return dto;
		}

		public static MediaLibrary? ToEntity(this MediaLibraryDto? dto)
		{
			if (dto == null)
			{
				return null;
			}

			var entity = new MediaLibrary
			{
				Id = dto.Id,
				CategoryId = dto.CategoryId,
				ResourceCount = dto.ResourceCount,
				Name = dto.Name,
				Order = dto.Order,
				PathConfigurationsJson = dto.PathConfigurations.Serialize()
			};
			if (dto.PathConfigurations?.Any() == true)
			{
				try
				{
					entity.PathConfigurationsJson =
						JsonConvert.SerializeObject(dto.PathConfigurations.Select(pc => pc.ToEntity()));
				}
				catch (Exception)
				{
					// ignored
				}
			}

			return entity;
		}

		public static MediaLibraryDto Duplicate(this MediaLibraryDto ml, int toCategoryId)
		{
			return ml with
			{
				Id = 0,
				CategoryId = toCategoryId,
				ResourceCount = 0
			};
		}
	}
}