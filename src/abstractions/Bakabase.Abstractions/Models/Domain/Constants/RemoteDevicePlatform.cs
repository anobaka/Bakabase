namespace Bakabase.Abstractions.Models.Domain.Constants;

/// <summary>
/// The OS family of a paired remote device. Drives which native-player hand-off
/// links the UI offers for that device.
/// </summary>
public enum RemoteDevicePlatform
{
    Unknown = 0,
    Windows = 1,
    MacOS = 2,
    Linux = 3,
    Android = 4,
    IOS = 5
}
