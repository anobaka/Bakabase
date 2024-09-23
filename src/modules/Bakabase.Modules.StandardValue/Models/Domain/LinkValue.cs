using System.Text.RegularExpressions;
using Bootstrap.Extensions;

namespace Bakabase.Modules.StandardValue.Models.Domain;

public record LinkValue(string? Text, string? Url)
{
    public string? Text { get; set; } = Text;
    public string? Url { get; set; } = Url;

    public override string? ToString()
    {
        var text = Text?.Trim();
        var url = Url?.Trim();
        if (string.IsNullOrEmpty(text) && string.IsNullOrEmpty(url))
        {
            return null;
        }

        if (string.IsNullOrEmpty(text))
        {
            return url;
        }

        if (string.IsNullOrEmpty(url))
        {
            return text;
        }

        return $"[{text}]({url})";
    }

    public bool IsEmpty => string.IsNullOrEmpty(Text?.Trim()) && string.IsNullOrEmpty(Url?.Trim());


    private static readonly Regex ParseRegex =
        new Regex(@"\[(?<text>.+?)\][\s\S]*\((?<url>.+?)\)", RegexOptions.Compiled);

    public static LinkValue? TryParse(string? text)
    {
        text = text?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var match = ParseRegex.Match(text);
        if (!match.Success)
        {
            return new LinkValue(text, null);
        }

        var t = match.Groups["text"].Value.Trim();
        var url = match.Groups["url"].Value.Trim();
        return new LinkValue(t.IsNullOrEmpty() ? null : t, url.IsNullOrEmpty() ? null : url);
    }
}