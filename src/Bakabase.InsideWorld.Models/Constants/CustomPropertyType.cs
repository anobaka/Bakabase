using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Models.Constants
{
	public enum CustomPropertyType
	{
		SingleLineText = 1,
		MultilineText,
		SingleChoice,
		MultipleChoice,
		Number,
		Percentage,
		Rating,
		Boolean,
		Link,
		Attachment,
		// Formula,
		// Multilevel,
	}
}
