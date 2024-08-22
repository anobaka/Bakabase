using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Models.Aos;
using Bootstrap.Extensions;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Naming
{
    public class DownloaderNamingFieldsExtractor<TNamingFieldsType>
    {
        public static DownloaderNamingDefinitions.Field[] AllFields = SpecificTypeUtils<TNamingFieldsType>.Type
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(fi => fi.IsLiteral && !fi.IsInitOnly).Select(a =>
            {
                var field = new DownloaderNamingDefinitions.Field
                {
                    Key = a.GetRawConstantValue() as string
                };
                var attr = a.GetCustomAttribute<DownloaderNamingFieldAttribute>();
                if (attr != null)
                {
                    field.Example = attr.Example;
                    field.Description = attr.Description;
                }

                return field;
            }).ToArray();

        private static Dictionary<string, string> _fieldsAndReplacers;

        public static Dictionary<string, string> FieldsAndReplacers =>
            _fieldsAndReplacers ??= AllFields.ToDictionary(a => a.Key, a => $"{{{a.Key}}}");
    }
}