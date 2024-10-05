namespace Bakabase.Modules.Property.Abstractions.Models.Db
{
	public record CustomPropertyValueDbModel
	{
		public int Id { get; set; }
		public int ResourceId { get; set; }
		public int PropertyId { get; set; }
		public string? Value { get; set; }
		public int Scope { get; set; }
    }
}