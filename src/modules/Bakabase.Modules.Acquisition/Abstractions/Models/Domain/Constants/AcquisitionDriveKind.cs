namespace Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;

/// <summary>
/// Which service a shared link points at. Inferred from the host name, and used to prefer one link
/// over another and to pick the step that knows how to fetch from it.
/// </summary>
public enum AcquisitionDriveKind
{
    Unknown = 0,
    DirectUrl = 1,
    Baidu = 2,
    Xunlei = 3,
    Feimao = 4,
    Cloudflare = 5,
    Mega = 6,
    PikPak = 7,
    GoogleDrive = 8,
    OneDrive = 9,
    Magnet = 10,
    OneOneFive = 11
}
