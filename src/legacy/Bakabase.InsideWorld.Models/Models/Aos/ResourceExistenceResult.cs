using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Models.Models.Aos
{
    public class ResourceExistenceResult
    {
        public ResourceExistence Existence { get; set; }
        public string[] Resources { get; set; }
    }
    public enum ResourceExistence
    {
        Exist = 1,
        Maybe = 2,
        New = 3
    }
}