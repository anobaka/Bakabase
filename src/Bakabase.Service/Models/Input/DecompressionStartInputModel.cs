using System.Collections.Generic;

namespace Bakabase.Service.Models.Input;

public record DecompressionStartInputModel
{
    public bool OnFailureContinue { get; set; }
    public DecompressionStartInputModelItem[] Items { get; set; } = [];
}