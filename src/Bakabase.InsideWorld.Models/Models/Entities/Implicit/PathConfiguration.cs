using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Aos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Models.Models.Entities.Implicit
{
	public record PathConfiguration
	{
		public string? Path { get; set; }

		public int[]? FixedTagIds { get; set; }

		/// <summary>
		/// Resource property matcher values
		/// </summary>
		public List<MatcherValue>? RpmValues { get; set; }
	}
}