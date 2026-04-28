using System.ComponentModel.DataAnnotations;

namespace Bakabase.Modules.DataCard.Models.Input;

public class DataCardFindByIdentityInputModel
{
    [Required]
    public int TypeId { get; set; }

    /// <summary>
    /// Optional. When set, matches with this card's id are ignored (used during edits).
    /// </summary>
    public int? ExcludeCardId { get; set; }

    public List<DataCardPropertyValueInputModel>? PropertyValues { get; set; }
}
