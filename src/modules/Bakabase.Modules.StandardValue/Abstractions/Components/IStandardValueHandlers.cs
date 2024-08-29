using Bakabase.Abstractions.Models.Domain.Constants;
using System.Diagnostics.CodeAnalysis;

namespace Bakabase.Modules.StandardValue.Abstractions.Components;

public interface IStandardValueHandlers
{
    IStandardValueHandler[] Handlers { get; }
    IStandardValueHandler this[StandardValueType type] { get; }
    bool TryGet(StandardValueType type, [MaybeNullWhen(false)] out IStandardValueHandler stdValueHandler);
}