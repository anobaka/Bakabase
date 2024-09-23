using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.StandardValue.Abstractions.Models.Domain.Constants;
using Bootstrap.Extensions;

namespace Bakabase.InsideWorld.Business.Extensions
{
    public static class ConversionExtensions
    {
        public static StandardValueConversionRule[] GetFlags(this StandardValueConversionRule loss)
        {
            return SpecificEnumUtils<StandardValueConversionRule>.Values.Where(f => loss.HasFlag(f)).ToArray();
        }
    }
}