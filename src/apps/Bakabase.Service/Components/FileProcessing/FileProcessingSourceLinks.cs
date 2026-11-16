using System.Text.RegularExpressions;

namespace Bakabase.Service.Components.FileProcessing;

internal static class FileProcessingSourceLinks
{
    internal static readonly Regex Address = new(@"https?://[^\s<>""']+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    internal static readonly Regex AccessCode = new(@"(?:提取[码碼]|访问[码碼]|訪問[码碼]|access code|[?&]pwd=)\s*[:：=]?\s*([a-zA-Z0-9_-]{1,64})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // Remove only the reference and its labelled code. Separate archive instructions
    // on the same line can legitimately supply the same literal.
    internal static string WithoutDownloadCredentials(string text) =>
        AccessCode.Replace(Address.Replace(text, " "), " ");
}
