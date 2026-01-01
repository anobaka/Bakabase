using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.Property.Models.View;

public record PropertyViewModel
{
    public PropertyPool Pool { get; set; }
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public PropertyType Type { get; set; }
    public object? Options { get; set; }
    public StandardValueType DbValueType => PropertySystem.Property.GetDbValueType(Type);
    public StandardValueType BizValueType => PropertySystem.Property.GetBizValueType(Type);
    public string PoolName { get; set; } = null!;
    public string TypeName { get; set; } = null!;
    public int Order { get; set; }
}