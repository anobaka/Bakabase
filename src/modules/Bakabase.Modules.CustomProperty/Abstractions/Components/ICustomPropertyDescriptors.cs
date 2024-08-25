using System.Diagnostics.CodeAnalysis;

namespace Bakabase.Modules.CustomProperty.Abstractions.Components;

public interface ICustomPropertyDescriptors
{
    ICustomPropertyDescriptor[] Descriptors { get; }
    ICustomPropertyDescriptor this[int type] { get; }
    bool TryGet(int type, [MaybeNullWhen(false)] out ICustomPropertyDescriptor propertyDescriptor);
}