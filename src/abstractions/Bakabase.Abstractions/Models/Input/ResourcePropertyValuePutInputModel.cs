namespace Bakabase.Abstractions.Models.Input
{
	public record ResourcePropertyValuePutInputModel
	{
		public int PropertyId { get; set; }
		public bool IsCustomProperty { get; set; }
		public string? Value { get; set; }
		/// <summary>
		/// When true, Value is treated as a serialized bizValue (e.g. label text for choices)
		/// and will be converted to dbValue via PrepareDbValue, auto-creating options if needed.
		/// When false (default), Value is treated as a serialized dbValue.
		/// </summary>
		public bool IsBizValue { get; set; }
	}
}
