using Bootstrap.Components.Configuration.Abstractions;
using OllamaSharp;

namespace Bakabase.Abstractions.Components.Ai;

public class OllamaApiClientAccessor(IBOptions<AiOptions>)
{
    private OllamaApiClient? _client;

    public OllamaApiClient Client
    {
        get
        {

        }
    }
}