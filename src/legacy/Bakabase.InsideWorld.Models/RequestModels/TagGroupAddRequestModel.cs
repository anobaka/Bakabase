using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Bakabase.InsideWorld.Models.RequestModels
{
    public class TagGroupAddRequestModel
    {
        [Required] public string[] Names { get; set; } = Array.Empty<string>();
    }
}