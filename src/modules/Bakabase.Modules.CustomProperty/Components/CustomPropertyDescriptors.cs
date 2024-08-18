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

    public ICustomPropertyDescriptor this[int id]
    {
        get
        {
            if (TryGet(id, out var cpd))
            {
                return cpd;
            }

            throw new DevException($"{nameof(ICustomPropertyDescriptor)} for {id} is not found");
        }
    }

    public bool TryGet(int id, [MaybeNullWhen(false)] out ICustomPropertyDescriptor propertyDescriptor) =>
        _descriptorMap.TryGetValue(id, out propertyDescriptor);
}