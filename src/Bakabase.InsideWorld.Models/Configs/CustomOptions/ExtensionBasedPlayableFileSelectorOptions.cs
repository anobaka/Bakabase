using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Bakabase.InsideWorld.Models.Configs.CustomOptions
{
    public class ExtensionBasedPlayableFileSelectorOptions
    {
        [BindRequired] [Required] public int MaxFileCount { get; set; }

        [Required]
        [MinLength(1)]
        [DefaultValue(new string[] { })]
        public string[] Extensions { get; set; }
    }
}