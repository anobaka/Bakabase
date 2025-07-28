using Bakabase.InsideWorld.Models.Configs;

namespace Bakabase.InsideWorld.Business.Components.Configurations.Models.Input;

public class JavLibraryOptionsPatchInputModel
{
    public string? Cookie { get; set; }
    public JavLibraryOptions.CollectorOptions? Collector { get; set; }
}