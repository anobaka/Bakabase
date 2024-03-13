using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.RequestModels;

namespace Bakabase.InsideWorld.Models.Models.Dtos.CustomProperty.Abstrations
{
	public abstract record CustomPropertyValueDto : ICustomPropertyValue
	{
		public int Id { get; set; }
		public int PropertyId { get; set; }
		public int ResourceId { get; set; }
		public CustomPropertyDto? Property { get; set; }
		public abstract bool IsMatch(CustomPropertyValueSearchRequestModel model);
	}

	public abstract record CustomPropertyValueDto<T> : CustomPropertyValueDto
	{
		public T? Value { get; set; }
	}
}