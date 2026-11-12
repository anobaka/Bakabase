using System;
using System.Collections.Generic;
using System.Linq;
using Bakabase.Abstractions.Components.Network;
using Bakabase.InsideWorld.Models.Constants;
using Microsoft.AspNetCore.Http;

namespace Bakabase.Service.Components.OAuth;

public interface IOAuthProvider
{
    string Id { get; }
    string Scopes { get; }
    string CallbackPath { get; }
    string ProxyToolId { get; }
    ThirdPartyId ProxyThirdPartyId { get; }
    string? NormalizeClientId(string? clientId);
    string NormalizeTenant(string? tenantId);
    Uri GetAuthorizationEndpoint(string tenantId);
    Uri GetTokenEndpoint(string tenantId);
    bool AreReturnedScopesAllowed(string scopes);
}

public sealed class MicrosoftGraphOAuthProvider : IOAuthProvider
{
    public const string ProviderId = "microsoftGraph";
    public const string RedirectPath = "/oauth/callback/microsoftGraph";
    public string Id => ProviderId;
    public string Scopes => "Files.Read Files.Read.All Sites.Read.All offline_access";
    public string CallbackPath => RedirectPath;
    public string ProxyToolId => DownloadToolProxyTargets.Rclone;
    public ThirdPartyId ProxyThirdPartyId => ThirdPartyId.OneDrive;
    public Uri GetAuthorizationEndpoint(string tenantId) => Endpoint(tenantId, "authorize");
    public Uri GetTokenEndpoint(string tenantId) => Endpoint(tenantId, "token");

    public string? NormalizeClientId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (!Guid.TryParseExact(value.Trim(), "D", out var id)) throw new OAuthAuthorizationException("OAUTH_CLIENT_ID_INVALID");
        return id.ToString("D");
    }

    public string NormalizeTenant(string? value)
    {
        value = string.IsNullOrWhiteSpace(value) ? "common" : value.Trim();
        if (value is "common" or "organizations" or "consumers") return value;
        if (Guid.TryParseExact(value, "D", out var id)) return id.ToString("D");
        throw new OAuthAuthorizationException("OAUTH_TENANT_INVALID");
    }

    private Uri Endpoint(string tenantId, string operation) =>
        new("https://login.microsoftonline.com/" + NormalizeTenant(tenantId) + "/oauth2/v2.0/" + operation);

    public bool AreReturnedScopesAllowed(string scopes)
    {
        var allowed = Scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        // Microsoft's August 2021 dynamic-consent behavior can also return prior consent.
        // Tolerate standard identity scopes without requesting/using them; never accept writes.
        // https://learn.microsoft.com/entra/identity-platform/reference-breaking-changes#august-2021
        allowed.UnionWith(["User.Read", "openid", "profile", "email"]);
        var returned = scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        const string prefix = "https://graph.microsoft.com/";
        return returned.Length > 0 && returned.All(scope =>
            allowed.Contains(scope.StartsWith(prefix, StringComparison.Ordinal) ? scope[prefix.Length..] : scope));
    }
}

public sealed class OAuthProviderRegistry
{
    private readonly IReadOnlyDictionary<string, IOAuthProvider> _providers;

    public OAuthProviderRegistry(IEnumerable<IOAuthProvider> providers)
    {
        var entries = providers.ToArray();
        if (entries.Any(p => string.IsNullOrWhiteSpace(p.Id) || p.Id.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_') ||
                             p.CallbackPath != "/oauth/callback/" + p.Id) ||
            entries.Select(p => p.Id).Distinct(StringComparer.Ordinal).Count() != entries.Length)
            throw new OAuthAuthorizationException("OAUTH_PROVIDER_REGISTRATION_INVALID");
        _providers = entries.ToDictionary(p => p.Id, StringComparer.Ordinal);
    }

    public IOAuthProvider Get(string providerId) => providerId != null && _providers.TryGetValue(providerId, out var provider)
        ? provider : throw new OAuthAuthorizationException("OAUTH_PROVIDER_UNSUPPORTED");

    // This is deliberately a reviewed ingress allowlist, not a public /oauth prefix.
    // A future provider must opt its exact route into the remote-access boundary too.
    public static bool IsCallbackRequest(HttpRequest request) => HttpMethods.IsGet(request.Method) &&
        IsRegisteredCallbackPath(request.Path);

    public static bool IsRegisteredCallbackPath(PathString path) => path.Value == MicrosoftGraphOAuthProvider.RedirectPath;
}
