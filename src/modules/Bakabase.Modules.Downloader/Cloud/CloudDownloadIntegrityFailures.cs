using System.Text.RegularExpressions;

namespace Bakabase.Modules.Downloader.Cloud;

public static partial class CloudDownloadIntegrityFailures
{
    public const string ChecksumMismatch = "CLOUD_DOWNLOAD_CHECKSUM_MISMATCH";
    public const string PartialIdentityUnverified = "CLOUD_PARTIAL_IDENTITY_UNVERIFIED";

    // The bridge reports these codes as InvalidOperationException. Match the declaration in
    // a stored exception chain, rather than treating a filename, URL or quoted message as a failure.
    [GeneratedRegex(@"^[ \t]*(?:---> (?:\(Inner Exception #\d+\) )?)?System\.InvalidOperationException: (?<code>" +
        ChecksumMismatch + "|" + PartialIdentityUnverified + @")(?=: |[ \t]*(?:<---)?\r?$)", RegexOptions.Multiline)]
    private static partial Regex BridgeFailure();

    public static bool RequiresManualRetry(string? message) =>
        message is not null && (message.Trim() is ChecksumMismatch or PartialIdentityUnverified ||
                                BridgeFailure().IsMatch(message));

    public static bool IsChecksumMismatch(string? message)
    {
        if (message == null) return false;
        if (message.Trim() == ChecksumMismatch) return true;
        var recorded = BridgeFailure().Match(message);
        return recorded.Success && recorded.Groups["code"].Value == ChecksumMismatch && !recorded.NextMatch().Success;
    }
}
