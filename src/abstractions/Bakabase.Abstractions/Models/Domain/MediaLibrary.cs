using Bakabase.InsideWorld.Models.Models.Aos;
using Bakabase.InsideWorld.Models.Models.Dtos;

namespace Bakabase.Abstractions.Models.Domain
{
	public record MediaLibrary
	{
		public int Id { get; set; }
		public string Name { get; set; } = string.Empty;
		public int CategoryId { get; set; }
		public int Order { get; set; }
		public int ResourceCount { get; set; }
	}
}