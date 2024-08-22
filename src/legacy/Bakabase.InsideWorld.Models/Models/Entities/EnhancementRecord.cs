using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bootstrap.Components.Cryptography;
using Newtonsoft.Json;

namespace Bakabase.InsideWorld.Models.Models.Entities
{
    public class EnhancementRecord
    {
        public int Id { get; set; }
        public int ResourceId { get; set; }

        public string? ResourceRawFullName { get; set; }
        /// <summary>
        /// For displaying only.
        /// </summary>
        public string? EnhancerName { get; set; }

        /// <summary>
        /// For searching, not comparing
        /// </summary>
        public string? EnhancerDescriptorId { get; set; }

        /// <summary>
        /// Enhancers(including their dynamic options) with same effects should have same <see cref="RuleId"/>s. Commonly, use <see cref="BuildRuleId"/> to build a <see cref="RuleId"/>.
        /// </summary>
        public string? RuleId { get; set; }

        public bool Success { get; set; }
        public string? Enhancement { get; set; }
        public string? Message { get; set; }
        public DateTime CreateDt { get; set; } = DateTime.Now;

        public static string BuildRuleId(string enhancerAssemblyQualifiedName, string enhancerVersion,
            string enhancerDataVersion) =>
            CryptographyUtils.Md5($"{enhancerAssemblyQualifiedName}-{enhancerVersion}-{enhancerDataVersion}");
    }
}