namespace Bakabase.Abstractions.Models.Input;

public record BulkDeleteResourcesInputModel
{
    public int[] Ids { get; set; } = [];

    public bool DeleteFiles { get; set; }
}
