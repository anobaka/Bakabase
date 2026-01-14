using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Comparison.Models.Domain.Constants;

namespace Bakabase.Modules.Comparison.Models.Input;

public record ComparisonRuleInputModel
{
    public int Order { get; set; }

    public PropertyPool PropertyPool { get; set; }
    public int PropertyId { get; set; }
    public PropertyValueScope? PropertyValueScope { get; set; }

    public ComparisonMode Mode { get; set; }
    public object? Parameter { get; set; }

    /// <summary>
    /// 是否在对比前进行文本标准化处理（忽略大小写、空格、特殊字符等）
    /// </summary>
    public bool Normalize { get; set; }

    public int Weight { get; set; }
    public bool IsVeto { get; set; }
    public double VetoThreshold { get; set; } = 1.0;

    public NullValueBehavior OneNullBehavior { get; set; } = NullValueBehavior.Skip;
    public NullValueBehavior BothNullBehavior { get; set; } = NullValueBehavior.Skip;
}
