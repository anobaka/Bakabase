using Bakabase.InsideWorld.Models.Constants;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Business.Components.Resource.Components.Player.Infrastructures
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    public class PlayerAttribute : ComponentAttribute
    {
        public override ComponentType Type => ComponentType.Player;
    }
}
