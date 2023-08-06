using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Models.Dtos;

namespace Bakabase.InsideWorld.Models.ResponseModels
{
    public class ResourcePreviewModel
    {
        public string CurrentDirectory { get; set; }
        public ResourceDto[] Resources { get; set; }
    }
}