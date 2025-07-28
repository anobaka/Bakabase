using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Input;

public record DownloadTaskDeleteInputModel(int[]? Ids = null, ThirdPartyId? ThirdPartyId = null);