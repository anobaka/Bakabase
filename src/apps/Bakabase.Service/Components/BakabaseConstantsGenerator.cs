using System;
using System.Linq;
using System.Text;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Network;
using System.Text.Json;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Wire;
using Bakabase.Modules.ThirdParty.Abstractions.Http;
using Bakabase.Modules.ThirdParty.ThirdParties.Av;
using Bootstrap.Components.Miscellaneous;

namespace Bakabase.Service.Components
{
    public static class BakabaseConstantsGenerator
    {
        /// <param name="extraEnums">
        /// Enums from assemblies this one does not reference. The relay's are the case:
        /// its failure header is read by the same frontend, but nothing on the server
        /// side can see it, so the codegen driver -- which sees both -- hands it in
        /// rather than the frontend keeping a hand-copied duplicate that would silently
        /// drift.
        /// </param>
        public static string Generate(params Type[] extraEnums)
        {
            var sb = new StringBuilder();
            sb.Append(ConstantsGenerator.Generate([..BakabaseConstantTypes.GetAll(), ..extraEnums]));
            sb.Append(Environment.NewLine);
            sb.Append(Environment.NewLine);
            sb.Append(GenerateExtensionMediaTypeMap());
            sb.Append(Environment.NewLine);
            sb.Append(GenerateAvSourceIds());
            sb.Append(Environment.NewLine);
            sb.Append(GenerateProxyTestSites());
            sb.Append(Environment.NewLine);
            sb.Append(GenerateProxyCapableThirdPartyIds());
            sb.Append(Environment.NewLine);
            sb.Append(GenerateDownloadToolProxyTargets());
            sb.Append(Environment.NewLine);
            sb.Append(GenerateCloudDownloadSources());
            sb.Append(Environment.NewLine);
            sb.Append(GenerateDataSyncConstants());
            return sb.ToString();
        }

        /// <summary>
        /// Data sync's build-time facts: the kinds in apply order, the wire contract this build speaks, and the
        /// option travel limit behind the "definition only" hint.
        /// </summary>
        private static string GenerateDataSyncConstants()
        {
            var nl = Environment.NewLine;
            var kinds = DataSyncKindIds.All.Select(k => $"\"{k}\"");
            return
                $"export const DataSyncKinds: readonly string[] = [{string.Join(", ", kinds)}] as const;{nl}" +
                $"export const DataSyncContractVersion = {DataSyncContract.Version};{nl}" +
                $"export const DataSyncMaxOptionsPerProperty = {DataSyncLimits.Default.MaxOptionsPerProperty};{nl}";
        }

        private static string GenerateProxyCapableThirdPartyIds()
        {
            var nl = Environment.NewLine;
            var entries = ProxyCapableThirdParties.All.Select(id => $"ThirdPartyId.{id}");

            return
                $"export const ProxyCapableThirdPartyIds: readonly ThirdPartyId[] = [{nl}" +
                $"  {string.Join($",{nl}  ", entries)}{nl}" +
                $"] as const;{nl}";
        }

        private static string GenerateDownloadToolProxyTargets()
        {
            var nl = Environment.NewLine;
            var entries = JsonSerializer.Serialize(DownloadToolProxyTargets.All, JsonSerializerOptions.Web);
            return $"export interface DownloadToolProxyTarget {{ id: string; name: string; thirdPartyId?: ThirdPartyId | null; supportedProxySchemes: readonly string[] }}{nl}" +
                   $"export const DownloadToolProxyTargets: readonly DownloadToolProxyTarget[] = {entries} as const;{nl}";
        }

        private static string GenerateCloudDownloadSources()
        {
            var nl = Environment.NewLine;
            var entries = JsonSerializer.Serialize(CloudDownloads.CloudSites.Sources, JsonSerializerOptions.Web);
            var availability = DownloadIntents.DownloadMethodCatalog.Methods.Where(m => m.Availability != null)
                .Select(m => new {MethodId = m.Id, m.ProviderId, m.Availability!.Status,
                    ReasonCode = DownloadIntents.DownloadMethodAvailabilityConstraint.ReasonCode, m.Availability.DescriptionKey});
            return $"export interface CloudDownloadMethod {{ id: string; displayName: string; engineId?: string | null; accountProvider?: string | null; sourceKinds: readonly string[]; dependencyIds: readonly string[]; scope?: string | null }}{nl}" +
                   $"export interface CloudDownloadSource {{ providerId: string; thirdPartyId: ThirdPartyId; supportsRemote: boolean; dependencyIds: readonly string[]; methods: readonly CloudDownloadMethod[] }}{nl}" +
                   $"export const CloudDownloadSources: readonly CloudDownloadSource[] = {entries} as const;{nl}" +
                   $"export interface DownloadMethodAvailabilityInfo {{ methodId: string; providerId: string; status: string; reasonCode: string; descriptionKey: string }}{nl}" +
                   $"export const DownloadMethodAvailability: readonly DownloadMethodAvailabilityInfo[] = {JsonSerializer.Serialize(availability, JsonSerializerOptions.Web)} as const;{nl}" +
                   $"export const DownloadSourceProviderIds = {JsonSerializer.Serialize(DownloadIntents.DownloadMethodCatalog.Methods.Select(m => m.ProviderId).Distinct().ToList())} as const;{nl}";
        }

        private static string GenerateProxyTestSites()
        {
            var nl = Environment.NewLine;
            var entries = ProxyTestSites.All
                .Select(s => $"  {{ id: \"{s.Id}\", name: \"{s.Name}\", url: \"{s.Url}\" }}")
                .ToList();
            var defaults = ProxyTestSites.DefaultSelectedIds.Select(id => $"\"{id}\"");

            return
                $"export interface ProxyTestSite {{ id: string; name: string; url: string }}{nl}" +
                $"{nl}" +
                $"export const ProxyTestSites: readonly ProxyTestSite[] = [{nl}" +
                string.Join("," + nl, entries) + nl +
                $"] as const;{nl}" +
                $"{nl}" +
                $"export const DefaultProxyTestSiteIds: readonly string[] = [{string.Join(", ", defaults)}] as const;{nl}";
        }

        private static string GenerateExtensionMediaTypeMap()
        {
            var nl = Environment.NewLine;
            var entries = InternalOptions.MediaTypeExtensions
                .SelectMany(kv => kv.Value.Select(ext => (Ext: ext, MediaType: kv.Key)))
                .OrderBy(t => t.Ext, StringComparer.OrdinalIgnoreCase)
                .Select(t => $"  \"{t.Ext}\": MediaType.{t.MediaType}")
                .ToList();

            return
                $"export const ExtensionMediaTypes: Record<string, MediaType> = {{{nl}" +
                string.Join("," + nl, entries) + nl +
                "};" + nl;
        }

        private static string GenerateAvSourceIds()
        {
            var nl = Environment.NewLine;
            var entries = AvSourceIds.All.Select(s => $"  \"{s}\"").ToList();
            return
                $"export const AvSourceIds: readonly string[] = [{nl}" +
                string.Join("," + nl, entries) + nl +
                "] as const;" + nl;
        }
    }
}
