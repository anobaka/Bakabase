using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Components.StandardValue;

public interface IStandardValueLocalizer
{
    string StandardValue_HandlerNotFound(StandardValueType type);
}