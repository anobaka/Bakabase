namespace Bakabase.Modules.DataCard.Abstractions.Models.Domain;

public record DataCardMatchRules
{
    public bool AutoBindEnabled { get; set; }
    public List<int>? MatchProperties { get; set; }
    public DataCardMatchMode MatchMode { get; set; } = DataCardMatchMode.Any;
    public bool AllowCreate { get; set; } = true;
    public bool AllowUpdate { get; set; } = true;
}

public enum DataCardMatchMode
{
    Any = 1,
    All = 2
}
