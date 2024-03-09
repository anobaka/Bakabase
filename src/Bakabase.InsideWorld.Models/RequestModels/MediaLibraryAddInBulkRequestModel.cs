using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Models.RequestModels
{
    public record MediaLibraryAddInBulkRequestModel
    {
        [Required] public Dictionary<string, string[]?> NameAndPaths { get; set; } = new();

        public void TrimSelf()
        {
            NameAndPaths = NameAndPaths.ToDictionary(x => x.Key.Trim(),
                    x => x.Value?.Select(y => y.Trim()).Where(y => !string.IsNullOrEmpty(y)).ToArray())
                .Where(x => !string.IsNullOrEmpty(x.Key)).ToDictionary(x => x.Key, x => x.Value);
        }
    }
}