using System.ComponentModel.DataAnnotations;
using Bakabase.Modules.DataCard.Abstractions.Models.Domain;

namespace Bakabase.Modules.DataCard.Models.Input;

public class DataCardTypeUpdateInputModel
{
    [MaxLength(256)]
    public string? Name { get; set; }
    public List<int>? PropertyIds { get; set; }
    public List<int>? IdentityPropertyIds { get; set; }
    public string? NameTemplate { get; set; }
    public DataCardMatchRules? MatchRules { get; set; }
    public int? Order { get; set; }
}
