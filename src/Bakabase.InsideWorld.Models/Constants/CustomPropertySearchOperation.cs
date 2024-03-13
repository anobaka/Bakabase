using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Models.Constants
{
	public enum CustomPropertySearchOperation
	{
		Equals = 1,
		NotEquals = 2,
		Contains = 3,
		NotContains = 4,
		Null = 5,
		NotNull = 6
	}
}
