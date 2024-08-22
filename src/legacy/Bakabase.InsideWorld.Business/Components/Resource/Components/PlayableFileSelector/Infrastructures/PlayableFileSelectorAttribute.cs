using System;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.InsideWorld.Business.Components.Resource.Components.PlayableFileSelector.Infrastructures
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    public class PlayableFileSelectorAttribute : ComponentAttribute
    {
        public override ComponentType Type => ComponentType.PlayableFileSelector;
    }
}
