namespace Bakabase.Abstractions.Models.Db
{
	public record CustomPropertyValue
	{
		public int Id { get; set; }
		public int ResourceId { get; set; }
		public int PropertyId { get; set; }
		public string? Value { get; set; }
	}
}