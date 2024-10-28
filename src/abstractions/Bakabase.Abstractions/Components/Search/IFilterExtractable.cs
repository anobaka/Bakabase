namespace Bakabase.Abstractions.Components.Search;

public interface IFilterExtractable<TGroup, TFilter> where TGroup : IFilterExtractable<TGroup, TFilter>
{
    List<TGroup>? Groups { get; }
    List<TFilter>? Filters { get; }
}