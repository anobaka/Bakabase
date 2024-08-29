using Bakabase.Abstractions.Exceptions;
using System.Diagnostics.CodeAnalysis;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.StandardValue.Abstractions.Components;

namespace Bakabase.Modules.StandardValue.Components;

public class StandardValueHandlers : IStandardValueHandlers
{
    public IStandardValueHandler[] Handlers { get; }
    private readonly Dictionary<StandardValueType, IStandardValueHandler> _handlersMap;

    public StandardValueHandlers(IEnumerable<IStandardValueHandler> descriptors)
    {
        Handlers = descriptors.ToArray();
        _handlersMap = Handlers.ToDictionary(d => d.Type, d => d);
    }

    public IStandardValueHandler this[StandardValueType type]
    {
        get
        {
            if (TryGet(type, out var cpd))
            {
                return cpd;
            }

            throw new DevException($"{nameof(IStandardValueHandler)} for {type} is not found");
        }
    }

    public bool TryGet(StandardValueType type, [MaybeNullWhen(false)] out IStandardValueHandler stdValueHandler) =>
        _handlersMap.TryGetValue(type, out stdValueHandler);
}