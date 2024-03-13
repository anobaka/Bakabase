using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Models.Models.Entities
{
	public record CustomPropertyValue
	{
		public int Id { get; set; }
		public int ResourceId { get; set; }
		public int PropertyId { get; set; }
		public string? Value { get; set; }
	}
}