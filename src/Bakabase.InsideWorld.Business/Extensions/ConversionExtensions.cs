using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Constants;
using Bootstrap.Extensions;

namespace Bakabase.InsideWorld.Business.Extensions
{
    public static class ConversionExtensions
    {
        public static StandardValueConversionLoss[] GetFlags(this StandardValueConversionLoss loss)
        {
            return SpecificEnumUtils<StandardValueConversionLoss>.Values.Where(f => loss.HasFlag(f)).ToArray();
        }
    }
}