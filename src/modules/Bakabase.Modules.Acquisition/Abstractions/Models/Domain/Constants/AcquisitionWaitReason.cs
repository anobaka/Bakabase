namespace Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;

/// <summary>
/// Why a step stopped and what the user is being asked for. The host shows the question; the step
/// says nothing about how it is asked.
/// </summary>
public enum AcquisitionWaitReason
{
    /// <summary>The file has to come from somewhere only a person can reach — a cloud drive
    /// behind a login and a captcha. The user puts it in the inbox directory.</summary>
    WaitingForFile = 1,

    /// <summary>Files arrived but none of them is clearly this task's.</summary>
    AmbiguousInboxFile = 2,

    /// <summary>The content costs money and the price is above what may be spent unattended.</summary>
    PaidContent = 3,

    /// <summary>Nothing downloadable was found in the shared content.</summary>
    NoLinks = 4,

    /// <summary>Several links were found and none is obviously the right one.</summary>
    ChooseLink = 5,

    /// <summary>Every password candidate was tried and the archive is still closed.</summary>
    PasswordUnknown = 6,

    /// <summary>The destination directory already exists with different content.</summary>
    TargetExists = 7,

    /// <summary>The user is picking a directory that already holds the files.</summary>
    PickDirectory = 8,

    /// <summary>A platform is fetching on its own — a download task, an install — and will say when it is done.</summary>
    PlatformFetch = 9
}
