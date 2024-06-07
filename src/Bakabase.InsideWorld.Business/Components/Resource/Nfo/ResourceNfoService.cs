using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Serialization;
using Bakabase.InsideWorld.Business.Components.Resource.Nfo.Models;
using Bakabase.InsideWorld.Business.Components.Resource.Nfo.Serializers.Infrastructures;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bootstrap.Extensions;
using NPOI.HSSF.Record.Chart;

namespace Bakabase.InsideWorld.Business.Components.Resource.Nfo
{
    public class ResourceNfoService
    {
        private static readonly XmlSerializer VersionSerializer = new(SpecificTypeUtils<VersionNfo>.Type);

        private static readonly Dictionary<int, IResourceNfoSerializer> Serializers = Assembly.GetExecutingAssembly()
            .GetTypes()
            .Where(t => !t.IsAbstract && t.IsClass &&
                        t.IsAssignableTo(SpecificTypeUtils<IResourceNfoSerializer>.Type) &&
                        t.Namespace!.StartsWith(SpecificTypeUtils<ResourceNfoService>.Type.Namespace!))
            .Select(t => Activator.CreateInstance(t) as IResourceNfoSerializer).ToDictionary(x => x!.Version, t => t!);

        public static string Serialize(Abstractions.Models.Domain.Resource resource)
        {
            var newestSerializer = Serializers[Serializers.Max(s => s.Key)];
            return newestSerializer.Serialize(resource);
        }

        public static async Task<Abstractions.Models.Domain.Resource> Deserialize(string xmlOrPath)
        {
            if (xmlOrPath.IsNullOrEmpty())
            {
                return null;
            }

            var isPath = false;
            try
            {
                Path.GetFullPath(xmlOrPath);
                isPath = true;
            }
            catch
            {
                // ignored
            }

            string xml;
            if (isPath)
            {
                if (!File.Exists(xmlOrPath))
                {
                    return null;
                }
                else
                {
                    xml = await File.ReadAllTextAsync(xmlOrPath);
                }
            }
            else
            {
                xml = xmlOrPath;
            }

            var stringReader = new StringReader(xml);
            var document = XDocument.Load(stringReader);
            var layer1Elements = document.Root?.Elements();
            var versionElement = layer1Elements?.FirstOrDefault(t =>
                "Version".Equals(t.Name.ToString(), StringComparison.OrdinalIgnoreCase));

            var version = 0;
            if (versionElement != null)
            {
                if (int.TryParse(versionElement.Value, out var v))
                {
                    version = v;
                }
            }

            if (!Serializers.TryGetValue(version, out var serializer))
            {
                throw new Exception(
                    $"Unable to deserialize {xmlOrPath} because there is not a serializer with version {version}");
            }

            var dto = serializer.Deserialize(xml);
            return dto;
        }

        public static string GetFullname(Abstractions.Models.Domain.Resource resource)
        {
            return resource.IsFile
                ? Path.Combine(resource.Directory, $"{Path.GetFileNameWithoutExtension(resource.FileName)}{Extension}")
                : Path.Combine(resource.Path, DefaultFilename);
        }

        public const string Extension = ".nfo";
        public const string DefaultFilename = $"resource{Extension}";
    }
}