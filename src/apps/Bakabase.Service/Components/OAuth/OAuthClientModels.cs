using System;

namespace Bakabase.Service.Components.OAuth;

public sealed class OAuthAuthorizationException(string code) : Exception(code)
{
    public string Code { get; } = code;
}

public sealed class OAuthProviderConfigurationViewModel
{
    public string ProviderId { get; init; } = "";
    public string Mode { get; init; } = "manual";
    public string? ClientId { get; init; }
    public string? TenantId { get; init; }
    public string? CallbackUrl { get; init; }
    public bool HasClientSecret { get; init; }
    public bool IsReady { get; init; }
    public string? Problem { get; init; }
}

public sealed class OAuthProviderConfigurationInputModel
{
    public string Mode { get; init; } = "manual";
    public string? ClientId { get; init; }
    public string? TenantId { get; init; }
    public string? CallbackUrl { get; init; }
    public string? ClientSecret { get; init; }
    public bool ClearClientSecret { get; init; }

    public override string ToString() => "OAuthProviderConfigurationInputModel(credentials=[redacted])";
}

/// <summary>Private, immutable identity for one authorization, never an API response.</summary>
internal sealed record OAuthClientSnapshot
{
    public required string ProviderId { get; init; }
    public required string ConfigurationVersion { get; init; }
    public required string ClientId { get; init; }
    public required string ClientSecret { get; init; }
    public required string TenantId { get; init; }
    public required string CallbackUrl { get; init; }
    public required string Scopes { get; init; }
    public required string AuthEndpoint { get; init; }
    public required string TokenEndpoint { get; init; }

    public override string ToString() => $"OAuthClientSnapshot(provider={ProviderId}, version={ConfigurationVersion}, credentials=[redacted])";
}
