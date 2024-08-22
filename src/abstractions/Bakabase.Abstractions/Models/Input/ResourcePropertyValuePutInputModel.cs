namespace Bakabase.Abstractions.Models.Input
{
	public record ResourcePropertyValuePutInputModel
	{
		public int PropertyId { get; set; }
		public bool IsCustomProperty { get; set; }
		public string? Value { get; set; }
	}
}
