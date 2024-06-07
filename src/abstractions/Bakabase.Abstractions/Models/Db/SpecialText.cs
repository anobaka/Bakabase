using System.ComponentModel.DataAnnotations;
using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Models.Db
{
	public record SpecialText
	{
		[Key]
		public int Id { get; set; }

        [Required, MaxLength(64)] public string Value1 { set; get; } = null!;

		[MaxLength(64)]
		public string? Value2 { set; get; }

		public SpecialTextType Type { set; get; }
	}
}