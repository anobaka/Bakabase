namespace Bakabase.Service.Models.Input;

public record UserStoragePathsInputModel
{
    public string[] Paths { get; set; } = [];
}
