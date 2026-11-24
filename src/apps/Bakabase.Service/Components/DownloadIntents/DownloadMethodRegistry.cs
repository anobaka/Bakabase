using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Dependency.Abstractions;
using Bakabase.InsideWorld.Business.Components.Dependency.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.CloudDownloads.Baidu;
using Bakabase.Service.Components.CloudDownloads.Rclone;
using Bakabase.Service.Components.CloudDownloads.PikPakCli;
using Bootstrap.Components.Configuration.Abstractions;

namespace Bakabase.Service.Components.DownloadIntents;

/// <summary>Reviewed resolvers normalize locators; method descriptors describe local prerequisites.</summary>
public sealed class DownloadMethodRegistry(IEnumerable<ICloudDownloadProvider> providers,
    IEnumerable<IDependentComponentService> dependencies, BaiduPcsAccountStore accounts,
    IBOptions<ExHentaiOptions> exHentai, DownloadSourceResolver? resolver = null, RcloneAccountStore? rcloneAccounts = null,
    PikPakCliAccountService? pikpakAccounts = null,
    IEnumerable<ICloudDownloadAccountResolver>? accountResolvers = null)
{
    private readonly IReadOnlyList<ICloudDownloadProvider> _providers = providers.ToList();
    private readonly Dictionary<string, IDependentComponentService> _dependencies = dependencies.ToDictionary(d => d.Id);
    private readonly Dictionary<string, ICloudDownloadAccountResolver> _accountResolvers =
        (accountResolvers ?? []).ToDictionary(a => a.AccountProvider, StringComparer.Ordinal);

    private readonly DownloadSourceResolver _resolver = resolver ?? new([]);

    public Task<CloudSource> ResolveAsync(DownloadCandidate candidate, CancellationToken ct) =>
        _resolver.ResolveAsync(candidate, ct);

    public Task<CloudSource> ResolveAsync(CloudSource source, CancellationToken ct) =>
        _resolver.ResolveAsync(source, ct);

    public static string ReadinessKey(CloudSource source) =>
        string.Join(":", source.ProviderId, source.MethodId, source.AccountId, source.AccountVersion,
            DownloadMethodCatalog.Select(source)?.ValidateSource?.Invoke(source)?.Code ?? "ready",
            DownloadMethodCatalog.Select(source)?.Availability?.Status);

    public static CloudSource Normalize(DownloadCandidate candidate) => DownloadSourceResolver.Normalize(candidate);

    public static string MethodId(string provider) => DownloadMethodCatalog.Find(provider)?.Id ?? "manual";

    public async Task<IReadOnlyList<DownloadMethodView>> DescribeAllAsync(CloudSource source, string? accountId, CancellationToken ct)
    {
        var descriptors = DownloadMethodCatalog.FindAll(source);
        if (descriptors.Count == 0) return [await DescribeAsync(source, accountId, ct)];
        var result = new List<DownloadMethodView>();
        foreach (var descriptor in descriptors)
            result.Add(await DescribeAsync(source with {MethodId = descriptor.Id}, accountId, ct));
        return result;
    }

    /// <summary>Resolve a local account reference and freeze its generation before any artifact reuse or execution.</summary>
    public async Task<CloudSource> BindAccountAsync(CloudSource source, string? fallbackAccount, CancellationToken ct)
    {
        var descriptor = DownloadMethodCatalog.Select(source) ?? throw new ArgumentException("Choose a supported download method.");
        var selected = source with {MethodId = descriptor.Id, MethodScope = descriptor.ArtifactScope};
        if (descriptor.AccountProvider == null) return selected with {AccountId = null, AccountVersion = null};
        var requested = source.AccountId ?? fallbackAccount;
        if (_accountResolvers.TryGetValue(descriptor.AccountProvider, out var accountResolver))
        {
            var reference = await accountResolver.ResolveAccountAsync(requested, source.AccountVersion, ct);
            return selected with {AccountId = reference.AccountId, AccountVersion = reference.AccountVersion};
        }
        if (descriptor.AccountProvider == "baidu")
            return selected with {AccountId = await accounts.ResolveAccountIdAsync(requested, ct), AccountVersion = null};
        if (descriptor.AccountProvider == "pikpakCli")
        {
            var account = pikpakAccounts == null ? null : await pikpakAccounts.GetAsync(ct);
            if (account?.IsConfigured != true || account.Problem != null || account.AccountId == null || account.AccountVersion == null)
                throw new InvalidOperationException(account?.Problem ?? "PIKPAK_ACCOUNT_REQUIRED");
            if (requested != null && requested != account.AccountId || source.AccountVersion != null && source.AccountVersion != account.AccountVersion)
                throw new InvalidOperationException("PIKPAK_ACCOUNT_CHANGED");
            return selected with {AccountId = account.AccountId, AccountVersion = account.AccountVersion};
        }
        if (descriptor.AccountProvider == "exHentai") return selected;
        if (descriptor.EngineId != "rclone") throw new InvalidOperationException("CLOUD_ACCOUNT_REQUIRED");
        var store = rcloneAccounts ?? throw new InvalidOperationException("The rclone account service is not registered.");
        try
        {
            var id = await store.ResolveAccountIdAsync(descriptor.AccountProvider, requested, ct);
            var version = await store.GetConfiguredProfileVersionAsync(descriptor.AccountProvider, id, ct);
            if (source.AccountVersion != null && (source.AccountId != id || source.AccountVersion != version))
                throw new InvalidOperationException("The account configuration changed. Start a new run to use the new account identity.");
            return selected with {AccountId = id, AccountVersion = version};
        }
        catch (RcloneRemoteException error) { throw new InvalidOperationException(error.Code, error); }
    }

    // Static routing evidence only. Account state and installed tools affect readiness separately.
    internal static DownloadRequirement? SourceSupportIssue(CloudSource source, DownloadMethodDescriptor? descriptor)
    {
        if (descriptor != null) return descriptor.ValidateSource?.Invoke(source);
        return source.ProviderId == "weiyun"
            ? new("WEIYUN_PUBLIC_SHARE_UNSUPPORTED", "source",
                "The official Weiyun MCP download interface only supports files in your own drive and cannot import external shares. Choose or add another source.",
                Actions: ["chooseSource"])
            : new("SOURCE_UNSUPPORTED", "source", "No automatic adapter supports this source. Keep it as an acquisition lead or download it manually.", Actions: ["manualDownload"]);
    }

    public async Task<DownloadMethodView> DescribeAsync(CloudSource source, string? accountId, CancellationToken ct)
    {
        var requirements = new List<DownloadRequirement>();
        var descriptor = DownloadMethodCatalog.Select(source);
        accountId = source.AccountId ?? accountId;
        var dependencyIds = descriptor?.DependencyIds ?? [];
        var sourceIssue = SourceSupportIssue(source, descriptor);
        var supported = sourceIssue == null;
        if (sourceIssue != null) requirements.Add(sourceIssue);
        if (descriptor?.Availability is { } limitation)
        {
            requirements.Add(limitation.Requirement());
            // Installing tools or reconnecting an account cannot resolve a known integration limitation.
            // Keep link support separate so this never becomes evidence that the share expired.
            return new(descriptor.Id, descriptor.DisplayName, source.ProviderId,
                supported ? descriptor.Transport : "manual", supported, false, supported ? "unavailable" : "unsupported",
                requirements, dependencyIds, descriptor.SupportsTransfer, descriptor.AccountProvider,
                descriptor.EngineId, descriptor.SourceKinds, descriptor.Scope);
        }
        foreach (var id in dependencyIds)
        {
            if (!_dependencies.TryGetValue(id, out var dependency))
            {
                requirements.Add(new("DEPENDENCY_UNAVAILABLE", "dependency", "This download tool is not registered on the server.", id, Actions: ["configure"]));
                continue;
            }
            if (!dependency.IsAvailableOnCurrentPlatform)
                requirements.Add(new("PLATFORM_UNSUPPORTED", "platform", "This tool is unavailable on the server's current operating system or architecture.", id, Actions: ["configure"]));
            else
            {
                try
                {
                    // Discovery never installs, including when a future adapter names a required component.
                    if (dependency.Status is not (DependentComponentStatus.Installed or DependentComponentStatus.Installing))
                        await dependency.Discover(ct);
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    requirements.Add(new("DEPENDENCY_REQUIRED", "dependency", error.Message, id,
                        Actions: dependency.CanAutoInstallOnCurrentPlatform ? ["installDependency", "checkAgain"] : ["configure", "checkAgain"]));
                }
                if (dependency.Status != DependentComponentStatus.Installed && requirements.All(r => r.ComponentId != id))
                    requirements.Add(new("DEPENDENCY_REQUIRED", "dependency", "Install or configure this download tool.", id, Actions: ["installDependency", "checkAgain"]));
            }
        }
        if (descriptor?.AccountProvider == "baidu")
        {
            try
            {
                var id = await accounts.ResolveAccountIdAsync(accountId, ct);
                var account = (await accounts.ListAsync(ct)).FirstOrDefault(a => a.Id == id);
                if (account?.IsLoggedIn != true)
                    requirements.Add(new("BAIDU_LOGIN_REQUIRED", "account", "Choose a logged-in Baidu account or log in here.", AccountProvider: "baidu", Actions: ["manageAccount", "checkAgain"]));
            }
            catch (Exception error) when (error is not OperationCanceledException)
            { requirements.Add(new("BAIDU_ACCOUNT_UNAVAILABLE", "account", error.Message, AccountProvider: "baidu", Actions: ["manageAccount", "checkAgain"])); }
        }
        if (descriptor?.AccountProvider == "exHentai" && string.IsNullOrWhiteSpace(exHentai.Value.Cookie))
            requirements.Add(new("SITE_ACCOUNT_REQUIRED", "account", "Configure the ExHentai account in third-party settings.", AccountProvider: "exHentai", Actions: ["configure", "checkAgain"]));
        if (descriptor?.AccountProvider is { } configuredProvider &&
            (_accountResolvers.ContainsKey(configuredProvider) ||
             configuredProvider is not ("baidu" or "pikpakCli" or "exHentai") && descriptor.EngineId != "rclone"))
        {
            try { _ = await BindAccountAsync(source, accountId, ct); }
            catch (Exception error) when (error is not OperationCanceledException)
            { requirements.Add(new("CLOUD_ACCOUNT_REQUIRED", "account", "Connect the selected provider account in third-party settings, then check again.",
                AccountProvider: configuredProvider, Actions: ["manageAccount", "checkAgain"])); }
        }
        if (descriptor?.AccountProvider == "pikpakCli")
        {
            try { _ = await BindAccountAsync(source, accountId, ct); }
            catch (Exception error) when (error is not OperationCanceledException)
            { requirements.Add(new("PIKPAK_ACCOUNT_REQUIRED", "account", error.Message, AccountProvider: "pikpakCli", Actions: ["manageAccount", "checkAgain"])); }
        }
        if (descriptor?.EngineId == "rclone" && descriptor.AccountProvider != null)
        {
            try { _ = await BindAccountAsync(source, accountId, ct); }
            catch (Exception error) when (error is not OperationCanceledException)
            { requirements.Add(new("RCLONE_ACCOUNT_REQUIRED", "account", error.Message, AccountProvider: descriptor.AccountProvider, Actions: ["manageAccount", "checkAgain"])); }
        }
        var provider = DownloadProviderRouting.Find(_providers, source);
        if (requirements.Count == 0 && provider != null)
        {
            try
            {
                var ready = await provider.CheckReadyAsync(descriptor?.AccountProvider != null ? accountId : null, ct);
                if (!ready.Available) requirements.Add(new("ADAPTER_NOT_READY", "configuration", ready.Problem ?? "The adapter is not ready.",
                    dependencyIds.FirstOrDefault(), descriptor?.AccountProvider, ["checkAgain"]));
            }
            catch (CloudSourceNetworkException error)
            {
                requirements.Add(new(CloudSourceNetworkException.RequirementCode, "configuration", error.Message,
                    Actions: ["configure", "checkAgain"]));
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {requirements.Add(new("ADAPTER_NOT_READY", "configuration", error.Message, Actions: ["checkAgain"]));}
        }
        // A descriptor without a registered implementation is not an executable method.
        if (supported && descriptor?.Transport == "site" && descriptor.Id != "site.exHentai" &&
            provider == null)
            requirements.Add(new("ADAPTER_NOT_READY", "configuration", "This source adapter is not registered on the server.", Actions: ["configure"]));
        return new(descriptor?.Id ?? "manual", descriptor?.DisplayName ?? "Manual download", source.ProviderId,
            supported ? descriptor!.Transport : "manual", supported,
            supported && requirements.Count == 0, !supported ? "unsupported" : requirements.Count == 0 ? "ready" : "needsSetup",
            requirements, dependencyIds, descriptor?.SupportsTransfer ?? false, descriptor?.AccountProvider,
            descriptor?.EngineId, descriptor?.SourceKinds, descriptor?.Scope);
    }
}
