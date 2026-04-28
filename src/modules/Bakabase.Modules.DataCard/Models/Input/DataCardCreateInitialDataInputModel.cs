namespace Bakabase.Modules.DataCard.Models.Input;

public class DataCardCreateInitialDataInputModel
{
    public bool OnlyFromResources { get; set; }
    /// <summary>
    /// Property IDs that allow null values when generating combinations.
    /// Properties not in this list will skip combinations where their value is null.
    /// </summary>
    public List<int>? AllowNullPropertyIds { get; set; }
}
