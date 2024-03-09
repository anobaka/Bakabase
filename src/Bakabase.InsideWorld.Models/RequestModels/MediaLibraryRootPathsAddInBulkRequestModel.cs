using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Models.RequestModels
{
    public record MediaLibraryRootPathsAddInBulkRequestModel
    {
        public string[] RootPaths { get; set; } = [];

        public void TrimSelf()
        {
            RootPaths = RootPaths.Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToArray();
        }
    }
}
