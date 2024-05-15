using System.Collections.Generic;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.InsideWorld.Business.Components.StandardValue;

public interface IStandardValueService
{
    Task<Dictionary<object, object>> IntegrateWithAlias(Dictionary<object, StandardValueType> values);
}