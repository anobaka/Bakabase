namespace Bakabase.Modules.HealthScore.Components.Predicates;

public enum ComparisonOperator
{
    Equals = 1,
    NotEquals = 2,
    GreaterThan = 3,
    LessThan = 4,
    GreaterThanOrEquals = 5,
    LessThanOrEquals = 6,
}

internal static class ComparisonOperatorExtensions
{
    public static bool Apply(this ComparisonOperator op, long lhs, long rhs) => op switch
    {
        ComparisonOperator.Equals => lhs == rhs,
        ComparisonOperator.NotEquals => lhs != rhs,
        ComparisonOperator.GreaterThan => lhs > rhs,
        ComparisonOperator.LessThan => lhs < rhs,
        ComparisonOperator.GreaterThanOrEquals => lhs >= rhs,
        ComparisonOperator.LessThanOrEquals => lhs <= rhs,
        _ => false,
    };
}
