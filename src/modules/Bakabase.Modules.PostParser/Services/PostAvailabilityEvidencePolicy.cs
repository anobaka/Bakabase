using System.Text.RegularExpressions;
using Bakabase.Modules.PostParser.Models.Domain;

namespace Bakabase.Modules.PostParser.Services;

/// <summary>
/// A literal quote is necessary but does not by itself prove that a download expired.
/// This conservative policy is shared by new assessments, saved-result projections and task filters.
/// Unsupported or ambiguous language stays unknown; it never authorizes a purchase.
/// </summary>
public static class PostAvailabilityEvidencePolicy
{
    public const string InsufficientEvidence = "POST_AVAILABILITY_EVIDENCE_INSUFFICIENT";
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    private static readonly Regex DownloadSubject = new(
        @"链接|鏈接|连接|連接|分享|资源|資源|文件|网盘|網盤|度盘|度盤|压缩包|壓縮包|\b(?:links?|files?|downloads?|shares?|archives?)\b", Options);
    private static readonly Regex Expiry = new(
        @"失效|过期|過期|不存在|取消|删除|刪除|无法访问|無法訪問|打不开|打不開|没了|沒了|挂了|掛了|炸了|\b(?:expired|invalid|unavailable|removed|deleted|dead|broken|not\s+found)\b", Options);
    private static readonly Regex FailedDownload = new(
        @"无法下载|無法下載|不能下载|不能下載|下载不了|下載不了|\b(?:cannot|can't|unable\s+to)\s+download\b", Options);
    private static readonly Regex Restoration = new(
        @"(?:已|已经|已經)(?:经|經)?(?:补档|補檔|上传|上傳|分享)|(?:重新)?(?:补档|補檔|上传|上傳|分享)(?:了|完成)|\b(?:reuploaded|re-uploaded|reposted|re-posted|restored)\b", Options);
    private static readonly Regex Uncertain = new(
        @"好像|似乎|可能|是不是|是否|会不会|會不會|不知道|听说|聽說|据说|據說|请|請|将|將|准备|準備|打算|稍后|稍後|会重新|會重新|求|麻烦|麻煩|希望|[吗嗎么麼]\s*[。!！]*$|\b(?:maybe|perhaps|possibly|might|please|will|can|could|would|should|needs?|going\s+to|could\s+you|can\s+you|is\s+it|has\s+it)\b", Options);
    private static readonly Regex Negated = new(
        @"(?:没有|沒有|并未|並未|并不|並不|并非|並非|不是|尚未|未曾|不曾|没|沒|不|未).{0,5}(?:失效|过期|過期|不存在|取消|删除|刪除|无法|無法|不能|补档|補檔|上传|上傳|恢复|恢復)|\b(?:not|never|isn't|isnt|hasn't|hasnt|wasn't|wasnt)\s+(?:(?:yet|actually|been)\s+)*(?:expired|invalid|unavailable|removed|deleted|dead|broken|reuploaded|re-uploaded|restored)\b", Options);

    public static string? EffectiveStatus(string? status, IEnumerable<string>? evidence)
    {
        if (status is not ("expired" or "restored")) return status;
        var quotes = evidence?.Where(e => !string.IsNullOrWhiteSpace(e) && e.Length <= 1000).Take(32) ?? [];
        return quotes.Any(quote => HasExplicitStatement(quote, status)) ? status : "unknown";
    }

    public static PostAvailabilityAssessment? Apply(PostAvailabilityAssessment? assessment)
    {
        if (assessment == null || EffectiveStatus(assessment.Status, assessment.Evidence) == assessment.Status)
            return assessment;
        return assessment with {Status = "unknown", Reason = InsufficientEvidence};
    }

    private static bool HasExplicitStatement(string quote, string status)
    {
        // Separate assertions from follow-up questions, e.g. "link expired; can you reupload?".
        // Quoted hypothetical text, requests and negated assertions are never affirmative proof.
        foreach (var clause in Regex.Split(quote, @"[。；;，,\r\n]+"))
        {
            if (clause.IndexOfAny(['?', '？', '“', '”', '「', '」', '『', '』', '"']) >= 0 ||
                Uncertain.IsMatch(clause) || Negated.IsMatch(clause)) continue;
            if (status == "restored" ? Restoration.IsMatch(clause) :
                FailedDownload.IsMatch(clause) || DownloadSubject.IsMatch(clause) && Expiry.IsMatch(clause))
                return true;
        }
        return false;
    }
}
