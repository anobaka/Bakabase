using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Models.Input
{
    public class SpecialTextAddInputModel
    {
        public SpecialTextType Type { get; set; }
        public string Value1 { get; set; } = null!;
        public string? Value2 { get; set; }
    }
}
