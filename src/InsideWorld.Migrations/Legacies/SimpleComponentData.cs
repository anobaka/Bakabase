using Bakabase.InsideWorld.Models.Constants;

namespace InsideWorld.Migrations.Legacies
{
    [Obsolete]
    public class SimpleComponentData
    {
        public ComponentType Type { get; set; }
        public string TypeKey { get; set; }
        public int CustomOptionsId { get; set; }
    }
}