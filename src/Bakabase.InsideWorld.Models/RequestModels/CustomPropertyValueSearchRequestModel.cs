using Bakabase.InsideWorld.Models.Constants;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Models.RequestModels
{
	public record CustomPropertyValueSearchRequestModel
	{
		public int PropertyId { get; set; }
		public CustomPropertySearchOperation Operation { get; set; }
		public string? Value { get; set; }
	}
}
