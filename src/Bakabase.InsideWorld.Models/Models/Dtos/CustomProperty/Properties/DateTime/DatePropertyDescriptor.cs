using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.InsideWorld.Models.Models.Dtos.CustomProperty.Properties.DateTime
{
    public class DatePropertyDescriptor : DateTimePropertyDescriptor
    {
        public override CustomPropertyType Type => CustomPropertyType.Date;
    }
}
