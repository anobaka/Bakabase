using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Models.RequestModels
{
	public record ResourcePropertyValuePutInputModel
	{
		public int PropertyId { get; set; }
		public bool IsCustomProperty { get; set; }
		public string? Value { get; set; }
	}
}
