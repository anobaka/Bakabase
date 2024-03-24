using Bakabase.InsideWorld.Models.RequestModels;
using Newtonsoft.Json;

namespace Bakabase.InsideWorld.Models.Models.Dtos.CustomProperty.Abstractions
{
	public abstract record CustomPropertyValueDto
	{
		public int Id { get; set; }
		public int PropertyId { get; set; }
		public int ResourceId { get; set; }
		public CustomPropertyDto? Property { get; set; }
		public abstract bool IsMatch(CustomPropertyValueSearchRequestModel model);
		public object? Value { get; set; }
	}

	public abstract record CustomPropertyValueDto<T> : CustomPropertyValueDto
	{
		private T? _value;

		public new T? Value
		{
			get => _value;
			set => base.Value = _value = value;
		}

		public override bool IsMatch(CustomPropertyValueSearchRequestModel model)
		{
			return IsMatch(string.IsNullOrEmpty(model.Value) ? default : JsonConvert.DeserializeObject<T>(model.Value),
				model);
		}

		protected abstract bool IsMatch(T? value, CustomPropertyValueSearchRequestModel model);
	}
}