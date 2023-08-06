using System.Text;
using Bakabase.InsideWorld.Models.Constants;
using Bootstrap.Extensions;

namespace Bakabase.InsideWorld.Models.Models.Dtos
{
    public class TagDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = null!;
        public string? PreferredAlias { get; set; }
        public string? Color { get; set; }
        public int Order { get; set; }
        public int GroupId { get; set; }
        public string? GroupName { get; set; }
        public string? GroupNamePreferredAlias { get; set; }

        public string DisplayName
        {
            get
            {
                var sb = new StringBuilder();
                var gn = GroupNamePreferredAlias ?? GroupName;
                if (gn.IsNotEmpty())
                {
                    sb.Append(gn).Append(BusinessConstants.TagNameSeparator);
                }

                sb.Append(PreferredAlias ?? Name);
                return sb.ToString();
            }
        }
    }
}