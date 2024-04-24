namespace Bakabase.Abstractions.Models.Domain;

public record LinkData
{
    public string? Text { get; set; }
    public string? Url { get; set; }

    public override string? ToString()
    {
        if (string.IsNullOrEmpty(Text) && string.IsNullOrEmpty(Url))
        {
            return null;
        }

        if (string.IsNullOrEmpty(Text))
        {
            return Url;
        }

        if (string.IsNullOrEmpty(Url))
        {
            return Text;
        }

        return "[{Text}]({Url})";
    }
}