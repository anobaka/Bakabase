using Bakabase.Abstractions.Models.Domain.Constants;
using Bootstrap.Extensions;
using Newtonsoft.Json;

namespace Bakabase.Abstractions.Models.Db
{
	public record CustomPropertyValue
	{
		public int Id { get; set; }
		public int ResourceId { get; set; }
		public int PropertyId { get; set; }
		public string? Value { get; set; }
		public int Scope { get; set; }
    }
}