using System.ComponentModel.DataAnnotations;

namespace Bakabase.Abstractions.Models.Input
{
    public record MediaLibraryAddInBulkInputModel
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