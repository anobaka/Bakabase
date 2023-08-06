using System.ComponentModel.DataAnnotations;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.InsideWorld.Models.Models.Entities
{
	public class SpecialText
	{
		[Key]
		public int Id { get; set; }

		[Required, MaxLength(64)]
		public string Value1 { set; get; }

		[MaxLength(64)]
		public string? Value2 { set; get; }

		public SpecialTextType Type { set; get; }
	}
}