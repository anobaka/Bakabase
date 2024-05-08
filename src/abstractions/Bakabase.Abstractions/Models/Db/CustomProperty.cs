using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Models.Db
{
	public record CustomProperty
	{
		public int Id { get; set; }
		public string Name { get; set; } = null!;
		public int Type { get; set; }
		public DateTime CreatedAt { get; set; }
		public string? Options { get; set; }
	}
}