using System;
using Bakabase.Abstractions.Exceptions;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders;

/// <summary>An expected stop under the saved download policy, with an actionable user-facing explanation.</summary>
public sealed class DownloadPolicyException(string message) : InvalidOperationException(message), IUserActionableException;
