using Bakabase.InsideWorld.Models.Constants;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Models.Models.Dtos
{
	public record ResourceSearchFilterDto
	{
		public int PropertyId { get; set; }
		public bool IsReservedProperty { get; set; }
		public SearchOperation Operation { get; set; }
		public string? Value { get; set; }
	}
}
