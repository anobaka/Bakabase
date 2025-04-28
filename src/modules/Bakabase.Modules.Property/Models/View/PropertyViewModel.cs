using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Property.Extensions;

namespace Bakabase.Modules.Property.Models.View;

public record PropertyViewModel
{
    public PropertyPool Pool { get; set; }
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public PropertyType Type { get; set; }
    public object? Options { get; set; }
    public StandardValueType DbValueType => Type.GetDbValueType();
    public StandardValueType BizValueType => Type.GetBizValueType();
    public string PoolName { get; set; } = null!;
    public string TypeName { get; set; } = null!;
    public int Order { get; set; }
}