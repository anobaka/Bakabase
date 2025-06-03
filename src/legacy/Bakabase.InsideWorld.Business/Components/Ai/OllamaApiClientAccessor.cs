using System;
using Bakabase.InsideWorld.Business.Configurations.Models.Domain;
using Bootstrap.Components.Configuration;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.Extensions.Options;
using OllamaSharp;

namespace Bakabase.InsideWorld.Business.Components.Ai;

public class OllamaApiClientAccessor : IDisposable
{
    private readonly IDisposable _optionsEventHandler;
    private readonly IBOptions<AiOptions> _options;

    public OllamaApiClientAccessor(IBOptionsManager<AiOptions> options)
    {
        _options = options;
        var om = (options as AspNetCoreOptionsManager<AiOptions>)!;
        _optionsEventHandler = om.OnChange(_ => { _client = null; });
    }

    private OllamaApiClient? _client;

    public OllamaApiClient Client => _client ??= new OllamaApiClient(_options.Value.OllamaEndpoint!);

    public void Dispose()
    {
        _optionsEventHandler?.Dispose();
        _client?.Dispose();
    }
}