using Bakabase.InsideWorld.Models.Constants;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Bakabase.InsideWorld.Models.Models.Entities
{
    public class ComponentOptions
    {
        public int Id { get; set; }

        /// <summary>
        /// For display only, in case of component type is not found.
        /// </summary>
        [Required]
        [BindRequired]
        public ComponentType ComponentType { get; set; }

        [Required] public string ComponentAssemblyQualifiedTypeName { get; set; }
        [Required] public string Name { get; set; }
        public string? Description { get; set; }
        [Required] public string Json { get; set; }
    }
}