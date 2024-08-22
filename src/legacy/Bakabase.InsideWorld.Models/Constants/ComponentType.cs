using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Models.Constants
{
    public enum ComponentType
    {
        // [CategoryComponentTypeProperty(Required = false, MaxCount = int.MaxValue)]
        Enhancer = 1,

        // [CategoryComponentTypeProperty(Required = true, MaxCount = 1)]
        PlayableFileSelector = 2,

        // [CategoryComponentTypeProperty(Required = true, MaxCount = 1)]
        Player = 3
    }
}