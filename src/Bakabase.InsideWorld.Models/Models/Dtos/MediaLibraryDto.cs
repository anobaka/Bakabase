using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Extensions;
using Bakabase.InsideWorld.Models.Models.Aos;
using Bakabase.InsideWorld.Models.Models.Entities;

namespace Bakabase.InsideWorld.Models.Models.Dtos
{
	public record MediaLibraryDto
	{
		public int Id { get; set; }
		public string Name { get; set; } = string.Empty;
		public int CategoryId { get; set; }
		public int Order { get; set; }
		public int ResourceCount { get; set; }
		public Dictionary<string, MediaLibraryFileSystemInformation>? FileSystemInformation { get; set; }
		public ResourceCategoryDto? Category { get; set; }
		public List<PathConfigurationDto>? PathConfigurations { get; set; }

		public override string ToString()
		{
			return $"[{Id}]{Name}";
		}

		public static MediaLibraryDto CreateDefault(string name, int categoryId, params string[]? rootPaths)
		{
			return new MediaLibraryDto
			{
				Name = name,
				CategoryId = categoryId,
				Order = 0,
				ResourceCount = 0,
				PathConfigurations = rootPaths?.Select(PathConfigurationDto.CreateDefault).ToList()
			};
		}
	}
}