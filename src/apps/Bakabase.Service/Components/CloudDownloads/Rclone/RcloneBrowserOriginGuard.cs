using System;
using Bakabase.Service.Components.RemoteAccess;
using Microsoft.AspNetCore.Http;

namespace Bakabase.Service.Components.CloudDownloads.Rclone;

/// <summary>
/// A browser may authorize credentials only from the server UI (or the explicitly
/// enabled development UI). General userscript CORS permission does not confer this ability.
/// Ordinary remote-owner authorization remains independently required by the controller.
/// </summary>
public static class RcloneBrowserOriginGuard
{
    public const string RejectionCode = "RCLONE_BROWSER_ORIGIN_REJECTED";
    public static bool IsAllowed(HttpContext context) => OwnerUiOriginGuard.IsAllowed(context);

    public static void Ensure(HttpContext context)
    { if (!IsAllowed(context)) throw new RcloneRemoteException(RejectionCode); }

}

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class RcloneBrowserOriginAttribute() : OwnerUiOriginAttribute(RcloneBrowserOriginGuard.RejectionCode) { }
