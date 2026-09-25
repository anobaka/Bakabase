using System;
using Bakabase.Abstractions.Components.Network;
using Bakabase.Abstractions.Exceptions;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders.Bilibili;

// Messages here are localized user-facing texts; like everything a task message carries, they never contain
// a URL, a cookie or a response body.

/// <summary>The task's favorites folder is not among the logged-in account's folders. Fatal.</summary>
public sealed class BilibiliFavoritesNotFoundException(string message) : Exception(message), IUserActionableException;

/// <summary>
/// A run stopped for a reason the user can act on (risk control, an expired login, a full disk), with a localized
/// message. Transient exactly when <paramref name="inner"/> is (risk control is; the others are not).
/// </summary>
public sealed class BilibiliDownloadInterruptedException(string message, Exception inner)
    : Exception(message, inner), IUserActionableException;

/// <summary>
/// ffmpeg, which every Bilibili download needs for merging, is being installed right now. Transient: the run is
/// repeated later instead of failing the task for good.
/// </summary>
public sealed class BilibiliDependencyNotReadyException(string message, Exception inner)
    : Exception(message, inner), ITransientServiceError, IUserActionableException;
