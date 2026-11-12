using System;
using System.Text.RegularExpressions;

namespace Bakabase.Service.Components.CloudDownloads.Rclone;

/// <summary>Only reviewed categories leave native configuration; upstream text stays in memory.</summary>
internal static class RcloneConfigurationErrors
{
    internal static string Classify(string text, string fallback = "RCLONE_CONFIGURATION_ANSWER_REJECTED")
    {
        // Neither the match nor the original response is included in the returned value.
        var value = text.Length > 65536 ? text[..65536] : text;
        bool Has(string pattern) => Regex.IsMatch(value, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (Has(@"\b(?:invalidAuthenticationToken|unauthenticated|authenticationFailed|invalid_grant|invalid_client|interaction_required)\b|\b401\s+(?:Unauthorized|UnauthorizedAccess)\b"))
            return "RCLONE_CONFIGURATION_AUTHENTICATION_FAILED";
        if (Has(@"\b(?:accessDenied|access_denied|Authorization_RequestDenied|forbidden|insufficientPrivileges|insufficient_scope)\b|\b403\s+Forbidden\b"))
            return "RCLONE_CONFIGURATION_ACCESS_DENIED";
        if (Has(@"\b(?:itemNotFound|resourceNotFound|notFound)\b|\b404\s+Not\s+Found\b"))
            return "RCLONE_CONFIGURATION_NOT_FOUND";
        if (Has(@"\b(?:invalidRequest|badRequest)\b|\b400\s+Bad\s+Request\b"))
            return "RCLONE_CONFIGURATION_REQUEST_INVALID";
        if (Has(@"\b(?:tooManyRequests|throttledRequest|activityLimitReached)\b|\b429\b"))
            return "RCLONE_CONFIGURATION_RATE_LIMITED";
        if (Has(@"\b(?:serviceNotAvailable|temporarilyUnavailable|internalServerError)\b|\b(?:500|502|503|504)\s+(?:Internal|Bad|Service|Gateway)\b"))
            return "RCLONE_CONFIGURATION_SERVICE_UNAVAILABLE";
        if (Has(@"\b(?:x509|no such host|network is unreachable|connection refused|connection reset|i/o timeout|context deadline exceeded|TLS handshake timeout|proxyconnect|dial tcp|unexpected EOF)\b"))
            return "RCLONE_CONFIGURATION_NETWORK_FAILED";
        return fallback;
    }
}
