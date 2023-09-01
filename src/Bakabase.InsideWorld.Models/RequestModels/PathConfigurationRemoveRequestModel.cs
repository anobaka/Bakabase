using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Models.Entities;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Bakabase.InsideWorld.Models.RequestModels
{
    public class PathConfigurationRemoveRequestModel
    {
        [Required][BindRequired] public int Index { get; set; } = -1;
    }
}