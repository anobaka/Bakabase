using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.App;

namespace Bakabase.Service.Components.OAuth;

/// <summary>Owner configuration, separate from account tokens and authorization sessions.</summary>
public sealed class OAuthClientConfigurationStore
{
    private readonly string _root;
    private readonly OAuthProviderRegistry _registry;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public OAuthClientConfigurationStore(AppService app, OAuthProviderRegistry registry)
        : this(Path.Combine(app.AppDataDirectory, "oauth-clients"), registry) { }
    internal OAuthClientConfigurationStore(string root, OAuthProviderRegistry registry)
    { _root = Path.GetFullPath(root); _registry = registry; }

    public async Task<OAuthProviderConfigurationViewModel> GetAsync(string providerId, CancellationToken ct)
    {
        var provider = _registry.Get(providerId);
        await _gate.WaitAsync(ct);
        try { return View(provider, await ReadAsync(provider.Id, ct)); }
        finally { _gate.Release(); }
    }

    public async Task<OAuthProviderConfigurationViewModel> SaveAsync(string providerId,
        OAuthProviderConfigurationInputModel input, CancellationToken ct)
    {
        var provider = _registry.Get(providerId);
        if (input.Mode is not ("manual" or "automatic")) throw new OAuthAuthorizationException("OAUTH_MODE_INVALID");
        if (input.ClearClientSecret && !string.IsNullOrEmpty(input.ClientSecret))
            throw new OAuthAuthorizationException("OAUTH_CLIENT_SECRET_INVALID");
        if (input.ClientSecret is {Length: > 0} secret && (secret.Length > 4096 || string.IsNullOrWhiteSpace(secret) || secret.Any(char.IsControl)))
            throw new OAuthAuthorizationException("OAUTH_CLIENT_SECRET_INVALID");
        await _gate.WaitAsync(ct);
        try
        {
            var previous = await ReadAsync(provider.Id, ct);
            var clientId = provider.NormalizeClientId(input.ClientId ?? previous.ClientId);
            var changedApplication = clientId != previous.ClientId;
            var next = new Entry
            {
                ProviderId = provider.Id, Mode = input.Mode, ConfigurationVersion = Guid.NewGuid().ToString("N"),
                ClientId = clientId,
                TenantId = Tenant(provider, input.TenantId ?? previous.TenantId),
                CallbackUrl = Callback(provider, input.CallbackUrl ?? previous.CallbackUrl),
                ClientSecret = input.ClearClientSecret || changedApplication && string.IsNullOrEmpty(input.ClientSecret)
                    ? null : string.IsNullOrEmpty(input.ClientSecret) ? previous.ClientSecret : input.ClientSecret
            };
            OAuthPrivateStorage.Directory(_root);
            await OAuthPrivateStorage.WriteAsync(PathFor(provider.Id), JsonSerializer.Serialize(next, Json), ct);
            return View(provider, next);
        }
        finally { _gate.Release(); }
    }

    internal async Task<OAuthClientSnapshot?> CaptureAsync(string providerId, CancellationToken ct)
    {
        var provider = _registry.Get(providerId);
        await _gate.WaitAsync(ct);
        try
        {
            var entry = await ReadAsync(provider.Id, ct);
            if (entry.Mode == "manual") return null;
            if (!Ready(entry)) throw new OAuthAuthorizationException("OAUTH_CONFIGURATION_NOT_READY");
            return new()
            {
                ProviderId = provider.Id, ConfigurationVersion = entry.ConfigurationVersion, ClientId = entry.ClientId!,
                ClientSecret = entry.ClientSecret!, TenantId = entry.TenantId, CallbackUrl = entry.CallbackUrl!, Scopes = provider.Scopes,
                AuthEndpoint = provider.GetAuthorizationEndpoint(entry.TenantId).AbsoluteUri,
                TokenEndpoint = provider.GetTokenEndpoint(entry.TenantId).AbsoluteUri
            };
        }
        finally { _gate.Release(); }
    }

    private async Task<Entry> ReadAsync(string providerId, CancellationToken ct)
    {
        var path = PathFor(providerId); OAuthPrivateStorage.RejectLinks(path);
        if (!File.Exists(path)) return new() {ProviderId = providerId, TenantId = _registry.Get(providerId).NormalizeTenant(null)};
        if (new FileInfo(path).Length > 65536) throw new OAuthAuthorizationException("OAUTH_CONFIGURATION_INVALID");
        try
        {
            var entry = JsonSerializer.Deserialize<Entry>(await File.ReadAllTextAsync(path, ct), Json);
            if (entry == null || entry.ProviderId != providerId || entry.Mode is not ("manual" or "automatic") ||
                !Guid.TryParseExact(entry.ConfigurationVersion, "N", out _))
                throw new OAuthAuthorizationException("OAUTH_CONFIGURATION_INVALID");
            var provider = _registry.Get(providerId);
            entry.ClientId = provider.NormalizeClientId(entry.ClientId); entry.TenantId = Tenant(provider, entry.TenantId);
            entry.CallbackUrl = Callback(provider, entry.CallbackUrl);
            if (entry.ClientSecret is { } secret && (secret.Length > 4096 || secret.Any(char.IsControl)))
                throw new OAuthAuthorizationException("OAUTH_CONFIGURATION_INVALID");
            return entry;
        }
        catch (JsonException) { throw new OAuthAuthorizationException("OAUTH_CONFIGURATION_INVALID"); }
    }

    private string PathFor(string providerId) => Path.Combine(_root, providerId + ".json");
    private static string Tenant(IOAuthProvider provider, string? value)
    {
        value = provider.NormalizeTenant(value);
        // The provider owns endpoint validation. No configuration supplies an endpoint URL.
        _ = provider.GetAuthorizationEndpoint(value); _ = provider.GetTokenEndpoint(value);
        return value;
    }
    private static string? Callback(IOAuthProvider provider, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = value.Trim();
        var schemeEnd = value.IndexOf("://", StringComparison.Ordinal);
        var pathStart = schemeEnd < 0 ? -1 : value.IndexOf('/', schemeEnd + 3);
        if (value.Length > 4096 || value.Any(char.IsControl) || value.Any(char.IsWhiteSpace) || value.Contains('\\') ||
            pathStart < 0 || value[pathStart..] != provider.CallbackPath ||
            !Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length != 0 ||
            uri.Host.Length == 0 || uri.AbsolutePath != provider.CallbackPath || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new OAuthAuthorizationException("OAUTH_CALLBACK_URL_INVALID");
        return uri.AbsoluteUri;
    }
    private static bool Ready(Entry entry) => entry.Mode == "automatic" && entry.ClientId != null &&
        !string.IsNullOrWhiteSpace(entry.ClientSecret) && entry.CallbackUrl != null;
    private static OAuthProviderConfigurationViewModel View(IOAuthProvider provider, Entry entry) => new()
    {
        ProviderId = provider.Id, Mode = entry.Mode, ClientId = entry.ClientId, TenantId = entry.TenantId,
        CallbackUrl = entry.CallbackUrl, HasClientSecret = !string.IsNullOrEmpty(entry.ClientSecret), IsReady = Ready(entry),
        Problem = entry.Mode == "automatic" && !Ready(entry) ? "OAUTH_CONFIGURATION_NOT_READY" : null
    };
    private sealed class Entry
    {
        public Entry() { }
        public string ProviderId { get; set; } = "";
        public string ConfigurationVersion { get; set; } = "";
        public string Mode { get; set; } = "manual";
        public string? ClientId { get; set; }
        public string? ClientSecret { get; set; }
        public string TenantId { get; set; } = "";
        public string? CallbackUrl { get; set; }
    }
}

internal static class OAuthPrivateStorage
{
    internal static void RejectLinks(string path)
    {
        for (var current = Path.GetFullPath(path); current != null; current = Path.GetDirectoryName(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new OAuthAuthorizationException("OAUTH_STORAGE_PATH_INVALID");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }
    internal static void Directory(string path)
    {
        RejectLinks(path); System.IO.Directory.CreateDirectory(path);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }
    internal static async Task WriteAsync(string path, string content, CancellationToken ct)
    {
        RejectLinks(path);
        var temporary = path + "." + Guid.NewGuid().ToString("N");
        try
        {
            var options = new FileStreamOptions {Mode = FileMode.CreateNew, Access = FileAccess.Write};
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            await using (var output = new FileStream(temporary, options))
            {
                await using (var writer = new StreamWriter(output, new UTF8Encoding(false), leaveOpen: true))
                { await writer.WriteAsync(content.AsMemory(), ct); await writer.FlushAsync(ct); }
                output.Flush(true);
            }
            RejectLinks(path); File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
