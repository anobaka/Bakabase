using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Aos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace InsideWorld.Migrations.V171.Legacy.Models
{
	public record CompatiblePathConfiguration
	{
		public string? Path { get; set; }

		public int[]? FixedTagIds { get; set; }

		/// <summary>
		/// Resource property matcher values
		/// </summary>
		public List<PropertyPathSegmentMatcherValue>? RpmValues { get; set; }

		public string? Regex { get; set; }

		public List<SegmentMatcher>? Segments { get; set; } = new();

		public record SegmentMatcher : PropertyPathSegmentMatcherValue
		{
			public bool IsReverse { get; set; }
			public ResourceProperty Type { get; set; }
		}
	}
}