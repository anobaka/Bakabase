using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.Service.Models.Input;

public record ResourceCoverSaveInputModel
{
    public string Base64String { get; set; } = null!;
    public CoverSaveMode SaveMode { get; set; }
}