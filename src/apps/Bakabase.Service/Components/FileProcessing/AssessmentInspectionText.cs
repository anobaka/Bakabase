using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Text.Encodings.Web;

namespace Bakabase.Service.Components.FileProcessing;

/// <summary>Task-local diagnostic text. File names, IDs and proposed values remain visible;
/// authentication credentials and URL tokens are never copied into task metadata.</summary>
internal static class AssessmentInspectionText
{
    internal const int PreviewLimit = 8192;
    internal const int ReadLimit = PreviewLimit * 3;
    private static readonly Regex Secrets = new("""
        (?ix)
        https?://[^\s<>"']+ |
        (?:bearer|basic)\s+[a-z0-9+/_.=-]+ |
        (?:[a-z0-9_-]*(?:password|passwd|passcode|token|secret|api[_-]?key|authorization|cookie|credential|extract(?:ion)?[_-]?code|access[_-]?code)|pwd|密码|口令|提取码|验证码)
           ["']?\s*(?:[:=：]|is\b|是)\s*(?:"(?:\\.|[^"\\])*"?|'[^']*'?|[^,\r\n}]+) |
        \b(?:sk-[a-z0-9_-]{10,}|eyJ[a-z0-9_-]+\.[a-z0-9_-]+\.[a-z0-9_-]+)
        """, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    private static readonly Regex JsonKeys = new("""
        "(?:\\.|[^"\\])*"(?=\s*:)
        """, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly JsonSerializerOptions Json = new() {Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping};

    internal static (string? Text, bool Truncated) Preview(string? text)
    {
        if (string.IsNullOrEmpty(text)) return (null, false);
        bool truncated = text.Length >= ReadLimit;
        text = text[..Math.Min(text.Length, ReadLimit)];
        try
        {
            text = JsonKeys.Replace(text, match =>
            {
                try { return JsonSerializer.Serialize(JsonSerializer.Deserialize<string>(match.Value), Json); }
                catch (JsonException) { return match.Value; }
            });
            text = Secrets.Replace(text, "[…]");
        }
        catch (RegexMatchTimeoutException) { return ("[…]", true); }
        text = new string(text.Where(c => !char.IsControl(c) || c is '\n' or '\r' or '\t').ToArray());
        return (text[..Math.Min(text.Length, PreviewLimit)], truncated || text.Length > PreviewLimit);
    }
}
