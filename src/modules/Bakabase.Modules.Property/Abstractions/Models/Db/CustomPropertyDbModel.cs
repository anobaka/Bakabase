using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.Property.Abstractions.Models.Db
{
	public record CustomPropertyDbModel
	{
		public int Id { get; set; }
		public string Name { get; set; } = null!;
		public PropertyType Type { get; set; }
		public DateTime CreatedAt { get; set; }
		public string? Options { get; set; }
	}
}