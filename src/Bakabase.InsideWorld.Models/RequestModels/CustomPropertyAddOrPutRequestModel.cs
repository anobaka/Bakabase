using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.InsideWorld.Models.RequestModels
{
	public record CustomPropertyAddOrPutRequestModel
	{
		public string DisplayName { get; init; } = null!;
		public CustomPropertyType Type { get; set; }
		public string? Options { get; set; }
	}
}
