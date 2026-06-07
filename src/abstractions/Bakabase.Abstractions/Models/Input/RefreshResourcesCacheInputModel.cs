namespace Bakabase.Abstractions.Models.Input;

public record RefreshResourcesCacheInputModel
{
    public int[] Ids { get; set; } = [];
}
