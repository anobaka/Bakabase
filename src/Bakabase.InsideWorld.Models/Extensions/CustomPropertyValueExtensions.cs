﻿using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Dtos.CustomProperty.Abstrations;
using Bakabase.InsideWorld.Models.Models.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Models.Dtos.CustomProperty.ValueHelpers;

namespace Bakabase.InsideWorld.Models.Extensions
{
	public static class CustomPropertyValueExtensions
	{
		public static Dictionary<CustomPropertyType, ICustomPropertyValueHelper> Helpers = new()
		{
			{CustomPropertyType.SingleChoice, new SingleChoicePropertyValueHelper()}
		};
	}
}
