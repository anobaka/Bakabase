namespace Bakabase.Abstractions.Models.Input
{
    public record MediaLibraryRootPathsAddInBulkInputModel
    {
        public string[] RootPaths { get; set; } = [];

        public void TrimSelf()
        {
            RootPaths = RootPaths.Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToArray();
        }
    }
}
