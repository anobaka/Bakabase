namespace Bakabase.Service.Models.Input;

public record DecompressionInputModel
{
    public bool OnFailureContinue { get; set; }
    public DecompressionInputModelItem[] Items { get; set; } = [];
}
