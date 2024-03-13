using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.InsideWorld.Models.Models.Entities
{
	public record CustomProperty
	{
		public int Id { get; set; }
		public string Key { get; set; } = null!;
		public string DisplayName { get; set; } = null!;
		public CustomPropertyType Type { get; set; }
		public DateTime CreatedAt { get; set; }
		public string? Options { get; set; }
	}
}