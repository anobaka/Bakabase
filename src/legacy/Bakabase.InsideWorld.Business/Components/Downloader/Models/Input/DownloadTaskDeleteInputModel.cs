using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Models.Input;

public record DownloadTaskDeleteInputModel(int[]? Ids = null, ThirdPartyId? ThirdPartyId = null);