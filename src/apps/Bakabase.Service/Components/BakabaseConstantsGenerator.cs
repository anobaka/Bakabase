using System;
using System.Linq;
using System.Text;
using Bakabase.Abstractions.Components.Configuration;
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
            return sb.ToString();
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
