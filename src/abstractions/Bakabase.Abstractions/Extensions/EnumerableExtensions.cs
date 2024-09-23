namespace Bakabase.Abstractions.Extensions;

public static class EnumerableExtensions
{
    public static TOut? FirstNotNullOrDefault<TIn, TOut>(this IEnumerable<TIn>? values, Func<TIn, TOut?> predicate)
    {
        if (values == null)
        {
            return default;
        }

        foreach (var v in values)
        {
            var @out = predicate(v);
            if (@out != null)
            {
                return @out;
            }
        }

        return default;
    }

    public static List<T>? ToNullIfEmpty<T>(this List<T>? values) => values?.Any() == true ? values : null;
}