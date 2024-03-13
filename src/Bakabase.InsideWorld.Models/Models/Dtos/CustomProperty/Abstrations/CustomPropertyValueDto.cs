using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Models.Models.Dtos.CustomProperty.Abstrations
{
	public record CustomPropertyValueDto: ICustomPropertyValue
	{
		public int Id { get; set; }
		public int PropertyId { get; set; }
		public int ResourceId { get; set; }
	}

	public record CustomPropertyValueDto<T> : CustomPropertyValueDto
	{
		public T? Value { get; set; }
	}
}