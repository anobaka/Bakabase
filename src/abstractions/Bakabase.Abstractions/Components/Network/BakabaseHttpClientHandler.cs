namespace Bakabase.Abstractions.Components.Network
{
    public class BakabaseHttpClientHandler: HttpClientHandler
    {
        public BakabaseHttpClientHandler(BakabaseWebProxy webProxy)
        {
            Proxy = webProxy;
        }
    }
}
