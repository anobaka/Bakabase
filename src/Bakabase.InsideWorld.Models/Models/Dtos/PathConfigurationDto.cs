using Bakabase.InsideWorld.Models.Models.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Models.Entities.Implicit;
using Bakabase.InsideWorld.Models.Models.Aos;

namespace Bakabase.InsideWorld.Models.Models.Dtos
{
	public record PathConfigurationDto
	{
		public string? Path { get; set; }

		public int[]? FixedTagIds { get; set; }

		/// <summary>
		/// Resource property matcher values
		/// </summary>
		public List<MatcherValue>? RpmValues { get; set; }

		public List<TagDto>? FixedTags { get; set; }

		public static PathConfigurationDto CreateDefault(string rootPath) => new PathConfigurationDto
		{
			Path = rootPath,
			RpmValues = [MatcherValue.BuildResourceAtFirstLayerAfterRootPath()]
		};
	}
}