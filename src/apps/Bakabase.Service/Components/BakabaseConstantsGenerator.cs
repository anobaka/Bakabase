using System;
using System.Linq;
using System.Text;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.Modules.ThirdParty.Abstractions.Http;
using Bakabase.Modules.ThirdParty.ThirdParties.Av;
using Bootstrap.Components.Miscellaneous;

namespace Bakabase.Service.Components
{
    public static class BakabaseConstantsGenerator
    {
        public static string Generate()
        {
            var sb = new StringBuilder();
            sb.Append(ConstantsGenerator.Generate(BakabaseConstantTypes.GetAll()));
            sb.Append(Environment.NewLine);
            sb.Append(Environment.NewLine);
            sb.Append(GenerateExtensionMediaTypeMap());
            sb.Append(Environment.NewLine);
            sb.Append(GenerateAvSourceIds());
            sb.Append(Environment.NewLine);
            sb.Append(GenerateProxyTestSites());
            sb.Append(Environment.NewLine);
            sb.Append(GenerateProxyCapableThirdPartyIds());
            return sb.ToString();
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
