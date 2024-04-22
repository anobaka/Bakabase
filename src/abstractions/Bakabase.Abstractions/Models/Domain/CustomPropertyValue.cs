using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.RequestModels;
using Newtonsoft.Json;

namespace Bakabase.Abstractions.Models.Domain
{
	public abstract record CustomPropertyValue
	{
		public int Id { get; set; }
		public int PropertyId { get; set; }
		public int ResourceId { get; set; }
		public CustomProperty? Property { get; set; }
		public object? Value { get; set; }
		public CustomPropertyValueLayer Layer { get; set; }
    }

	public abstract record TypedCustomPropertyValue<T> : CustomPropertyValue
	{
		private T? _value;

		public new T? Value
		{
			get => _value;
			set => base.Value = _value = value;
		}
	}
}