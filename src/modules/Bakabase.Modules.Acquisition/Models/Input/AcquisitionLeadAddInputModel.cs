using System.ComponentModel.DataAnnotations;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.Acquisition.Models.Input;

public record AcquisitionLeadAddInputModel
{
    public AcquisitionLeadKind Kind { get; set; }

    [Required] [MaxLength(2048)] public string Value { get; set; } = null!;

    public AcquisitionLeadOrigin Origin { get; set; } = AcquisitionLeadOrigin.User;

    [MaxLength(512)] public string? Note { get; set; }
}
