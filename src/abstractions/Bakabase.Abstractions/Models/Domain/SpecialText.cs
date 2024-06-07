using System.ComponentModel.DataAnnotations;
using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Models.Domain
{
    public record SpecialText
    {
        public int Id { get; set; }
        public string Value1 { set; get; } = null!;
        public string? Value2 { set; get; }
        public SpecialTextType Type { set; get; }
    }
}