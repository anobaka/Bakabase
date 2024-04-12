using Bakabase.Modules.CustomProperty.Properties.Multilevel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.Modules.CustomProperty.Extensions
{
    public static class MultilevelPropertyExtensions
    {
        public static string[]? FindLabel(this MultilevelDataOptions options, string id)
        {
            if (options.Id == id)
            {
                return [options.Value];
            }

            return options.Children?.Select(child => child.FindLabel(id)).OfType<string[]>()
                .Select(result => new[] {options.Value}.Concat(result).ToArray()).FirstOrDefault();
        }
    }
}