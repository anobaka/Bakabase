namespace Bakabase.Abstractions.Models.Domain.Constants
{
	public enum SearchOperation
	{
		Equals = 1,
		NotEquals,
		Contains,
		NotContains,
		StartsWith,
		NotStartsWith,
		EndsWith,
		NotEndsWith,
		GreaterThan,
		LessThan,
		GreaterThanOrEquals,
		LessThanOrEquals,
		IsNull,
		IsNotNull,
		In,
		NotIn,
		Matches,
		NotMatches
	}
}
