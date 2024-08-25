using System.Diagnostics.CodeAnalysis;
using Bakabase.Abstractions.Exceptions;
using Bakabase.Modules.CustomProperty.Abstractions.Components;

namespace Bakabase.Modules.CustomProperty.Components;

public class CustomPropertyDescriptors : ICustomPropertyDescriptors
{
    public ICustomPropertyDescriptor[] Descriptors { get; }
    private readonly Dictionary<int, ICustomPropertyDescriptor> _descriptorMap;

    public CustomPropertyDescriptors(IEnumerable<ICustomPropertyDescriptor> descriptors)
    {
        Descriptors = descriptors.ToArray();
        _descriptorMap = Descriptors.ToDictionary(d => d.Type, d => d);
    }

    public ICustomPropertyDescriptor this[int type]
    {
        get
        {
            if (TryGet(type, out var cpd))
            {
                return cpd;
            }

            throw new DevException($"{nameof(ICustomPropertyDescriptor)} for {type} is not found");
        }
    }

    public bool TryGet(int type, [MaybeNullWhen(false)] out ICustomPropertyDescriptor propertyDescriptor) =>
        _descriptorMap.TryGetValue(type, out propertyDescriptor);
}