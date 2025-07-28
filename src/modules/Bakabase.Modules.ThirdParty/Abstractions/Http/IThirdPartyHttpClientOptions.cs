namespace Bakabase.Modules.ThirdParty.Abstractions.Http
{
    public interface IThirdPartyHttpClientOptions
    {
        public int MaxConcurrency { get; }
        public int RequestInterval { get; }
        public string? Cookie { get; }
        public string? UserAgent { get; }
        public string? Referer { get; }

        public Dictionary<string, string>? Headers { get; }

        public const string DefaultUserAgent =
            "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/87.0.4280.141 Safari/537.36";
    }
}