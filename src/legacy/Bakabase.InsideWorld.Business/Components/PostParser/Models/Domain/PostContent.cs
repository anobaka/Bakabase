using System.Collections.Generic;

namespace Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain;

public record PostContent
{
    public string Title { get; set; } = null!;
    public string MainHtml { get; set; } = null!;
    public List<string> CommentHtmlList { get; set; } = [];
}
