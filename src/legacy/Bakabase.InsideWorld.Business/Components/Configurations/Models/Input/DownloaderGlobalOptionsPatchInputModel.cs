namespace Bakabase.InsideWorld.Business.Components.Configurations.Models.Input;

public class DownloaderGlobalOptionsPatchInputModel
{
    public bool? AutoStartAfterCreation { get; set; }

    /// <summary>Omit to retain the current value; an empty string clears the default.</summary>
    public string? DefaultDownloadPath { get; set; }
}
