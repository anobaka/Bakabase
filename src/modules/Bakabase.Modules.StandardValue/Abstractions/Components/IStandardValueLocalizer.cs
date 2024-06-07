using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.StandardValue.Abstractions.Components;

public interface IStandardValueLocalizer
{
    string StandardValue_HandlerNotFound(StandardValueType type);
}