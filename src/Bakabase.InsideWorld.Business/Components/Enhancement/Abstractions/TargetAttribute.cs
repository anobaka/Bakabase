using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.InsideWorld.Business.Components.Enhancement.Abstractions
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Enum)]
    public class TargetAttribute(StandardValueType ValueType) : Attribute
    {

    }
}