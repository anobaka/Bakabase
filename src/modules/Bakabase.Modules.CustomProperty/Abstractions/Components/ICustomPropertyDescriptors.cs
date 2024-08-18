using System.Diagnostics.CodeAnalysis;

namespace Bakabase.Modules.CustomProperty.Abstractions.Components;

public interface ICustomPropertyDescriptors
{
    ICustomPropertyDescriptor[] Descriptors { get; }
    ICustomPropertyDescriptor this[int id] { get; }
    bool TryGet(int id, [MaybeNullWhen(false)] out ICustomPropertyDescriptor propertyDescriptor);
}