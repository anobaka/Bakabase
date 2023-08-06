using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using JetBrains.Annotations;

namespace Bakabase.InsideWorld.Models.Models.Dtos
{
    public record ResourceCategoryEnhancementOptions
    {
        /// <summary>
        /// Properties - Types
        /// </summary>
        public Dictionary<string, List<string>> EnhancementPriorities { get; set; } = new();

        public List<string> DefaultPriority { get; set; } = new();

        public void Standardize(string[]? enhancerKeys)
        {
            enhancerKeys ??= Array.Empty<string>();

            var defaultPriority = DefaultPriority?.Where(a => enhancerKeys.Contains(a))
                .Concat(enhancerKeys.Except(DefaultPriority)).ToList() ?? enhancerKeys.ToList();
            DefaultPriority = defaultPriority;

            if (EnhancementPriorities != null)
            {
                foreach (var (_, p) in EnhancementPriorities)
                {
                    p.RemoveAll(a => !enhancerKeys.Contains(a));
                }
            }
        }
    }
}