using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Models.Dto
{
	public record CustomPropertyAddOrPutDto
	{
		public string Name { get; init; } = null!;
		public CustomPropertyType Type { get; set; }
		public string? Options { get; set; }
	}
}
