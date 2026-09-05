namespace Bakabase.InsideWorld.Business.Components.Configurations.Models.Input;

public class JavbusDownloaderOptionsPatchInputModel
{
    public int? Concurrency { get; set; }
    public int? DelayMs { get; set; }
    public int? SizeTolerancePercentage { get; set; }
    public bool? SaveCovers { get; set; }
    public string? CoverDirectory { get; set; }
}
