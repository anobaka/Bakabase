using System;
using System.Linq;

namespace Bakabase.Service.Components.Workflow.Fs;

/// <summary>
/// The non-optional defense line in front of every planned rename
/// (docs/file-cleaning-workflow.html §3.5). Always sanitizes against the WINDOWS rules, even on
/// other platforms — a name that is legal on Linux but not on Windows would otherwise plan fine
/// here and break the library the day it is copied to a Windows machine.
/// </summary>
public static class FileNameSanitizer
{
    private static readonly char[] InvalidChars = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    private static readonly string[] ReservedNames =
    [
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    ];

    /// <summary>
    /// Returns the name made safe: invalid and control characters become underscores, trailing
    /// dots/spaces are trimmed, reserved device names get a leading underscore. An empty result
    /// means the name cannot be repaired and the rename must become a conflict.
    /// </summary>
    public static string Sanitize(string name)
    {
        var chars = name.Select(c => c < 32 || InvalidChars.Contains(c) ? '_' : c).ToArray();
        var cleaned = new string(chars).Trim().TrimEnd('.', ' ');
        if (cleaned.Length == 0)
        {
            return "";
        }

        var stem = cleaned.Split('.', 2)[0];
        if (ReservedNames.Contains(stem, StringComparer.OrdinalIgnoreCase))
        {
            cleaned = "_" + cleaned;
        }

        return cleaned;
    }
}
