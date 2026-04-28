using System.ComponentModel.DataAnnotations;

namespace Bakabase.Modules.DataCard.Models.Input;

public class DataCardAddInputModel
{
    [Required]
    public int TypeId { get; set; }
    public List<DataCardPropertyValueInputModel>? PropertyValues { get; set; }
}

public class DataCardPropertyValueInputModel
{
    [Required]
    public int PropertyId { get; set; }
    public string? Value { get; set; }
    public int Scope { get; set; }
}
