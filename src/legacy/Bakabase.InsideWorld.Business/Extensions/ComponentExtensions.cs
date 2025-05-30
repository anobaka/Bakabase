using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Resource.Components.PlayableFileSelector;
using Bakabase.InsideWorld.Business.Components.Resource.Components.PlayableFileSelector.Infrastructures;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace Bakabase.InsideWorld.Business.Extensions
{
    public static class ComponentExtensions
    {
        public static HashSet<string>? TryGetExtensions(this IPlayableFileSelector selector)
        {
            if (selector is ExtensionBasedPlayableFileSelector e)
            {
                return e.Options?.Extensions?.ToHashSet();
            }

            return null;
        }
    }
}
