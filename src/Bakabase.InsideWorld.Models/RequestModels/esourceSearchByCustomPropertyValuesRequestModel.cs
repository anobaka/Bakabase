using Bakabase.InsideWorld.Models.Constants;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Models.RequestModels
{
	public record ResourceSearchByCustomPropertyValuesRequestModel
	{
		public ResourceSearchByCustomPropertyValuesCombination Combination { get; set; }
		public List<CustomPropertyValueSearchRequestModel>? CustomPropertyValueSearchModels { get; set; }
	}
}
