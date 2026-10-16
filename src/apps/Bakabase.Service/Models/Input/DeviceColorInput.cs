using System;
using System.Linq;

namespace Bakabase.Service.Models.Input;

public sealed class DeviceColorInput
{
    /// <summary>Six-digit RGB color, or null to restore the device's automatic color.</summary>
    public string? Color { get; set; }

    public static bool IsValidDeviceId(string id) => !string.IsNullOrWhiteSpace(id) && id.Length <= 128 &&
        id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    public bool IsValid() => Color == null || Color.Length == 7 && Color[0] == '#' &&
        Color.AsSpan(1).IndexOfAnyExcept("0123456789abcdefABCDEF") < 0;
}
