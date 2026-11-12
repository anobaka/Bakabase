using Bakabase.Modules.PostParser.Services;
using Bakabase.Modules.PostParser.Models.Domain;
using System.Text.RegularExpressions;

namespace Bakabase.InsideWorld.Business.Components.PostParser.Fetchers;

public sealed class SoulPlusPostAnalysisPromptContributor : IPostAnalysisPromptContributor, IPostContentDecisionValidator
{
    public string Source => "SoulPlus";
    public string Version => "soulplus-content-purpose-v1";
    public bool CanIgnore(PostContentLock block, PostContentBlockDecision decision)
    {
        var context = block.ContextText ?? "";
        // A proof or donation label does not make a mixed-purpose block dispensable.
        // Ambiguous mentions remain for review instead of silently dropping credentials.
        if (Regex.IsMatch(context,
                "密码|密碼|提取码|提取碼|解压码|解壓碼|下载链接|下載鏈接|下载地址|下載地址|必要步骤|必要步驟|必需步骤|必需步驟"))
            return false;
        return decision.Purpose switch
        {
            "purchaseProof" => Regex.IsMatch(context, @"自[购購](?:[证證]明|[凭憑][证證]|[截][图圖])|[购購][买買](?:[证證]明|[凭憑][证證]|[截][图圖])|(?:^|\s)自[购購]\s*[:：]?\s*$"),
            "donation" => Regex.IsMatch(context, "支持我|打赏|打賞|赞助|贊助|随意支持|隨意支持"),
            "supplement" => Regex.IsMatch(context, "可选|可選|非必须|非必須|不需要"),
            "unrelated" => Regex.IsMatch(context, "与本帖无关|與本帖無關|无关内容|無關內容|题外话|題外話"),
            _ => false
        };
    }
    public string Instructions => """
        SoulPlus帖子可能把购买记录、自购凭证或订单截图放在单独的收费块，供版主核验自购。
        只有该块附近明确标注“自购证明”“购买证明”“自购凭证”“自购截图”等证明用途，或“自购：”作为直接说明该独立块的标签，才可判为purchaseProof/optional。
        标题中的“自购”、作者泛泛说自己购买过、其它楼层的证明文字，以及极高标价本身，都不能证明当前块是凭证。
        “如果你想要支持我”等明确额外支持/打赏的独立收费块可判donation/optional；下载密码、提取码或必要步骤如果同时在该块中，仍是required。
        自购证明常使用极高SP币标价，但不能用价格阈值推断用途；没有用途证据的高价块保持unknown。
        帖子可能有多个独立资源、分卷、注册表或修复补丁；不能把正文外的回复一律当无关内容。解决运行/获取问题所必需的补丁或步骤仍需保留，是否可选须由原文说明。
        同一购买链接可能出现在多个不同价格的内容块中。只分析各块用途；不推断实际收费、一次能解锁哪些块，不取最低价、最高价或求和，也不把证明块的价格覆盖到资源块。
        只读到第一页时，不推断管理员身份、隐藏证明内容或后续楼层。对缺少明确局部证据的用途保持unknown。
        """;
}
