using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Models.Dto
{
	public record CustomPropertyAddOrPutDto
	{
		public string Name { get; init; } = null!;
		public int Type { get; set; }
		public string? Options { get; set; }
	}
}
