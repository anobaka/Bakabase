using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.InsideWorld.Models.RequestModels
{
    public class SpecialTextCreateRequestModel
    {
        public SpecialTextType Type { get; set; }
        public string Value1 { get; set; } = null!;
        public string? Value2 { get; set; }
    }
}
