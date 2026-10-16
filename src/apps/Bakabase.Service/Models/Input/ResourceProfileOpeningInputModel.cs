namespace Bakabase.Service.Models.Input;

public sealed record ResourceProfileOpeningInputModel
{
    public string? SourceDeviceId { get; init; }
    public string? LibraryEpoch { get; init; }
    public int[] ResourceIds { get; init; } = [];
}
