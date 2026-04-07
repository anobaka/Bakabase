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
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36";
    }
}