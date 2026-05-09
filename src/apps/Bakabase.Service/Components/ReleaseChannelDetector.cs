using Bakabase.Infrastructures.Components.App;
using Bakabase.Infrastructures.Components.App.Models.Constants;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;

namespace Bakabase.Service.Components;

/// <summary>
/// Maps the running build to one of three GA4 user-property values used to segment dashboards
/// (per design decision §16, item 1).
/// </summary>
public static class ReleaseChannelDetector
{
    public const string Stable = "stable";
    public const string Beta = "beta";
    public const string Dev = "dev";

    public static string Detect(IWebHostEnvironment env)
    {
        if (env.IsDevelopment())
        {
            return Dev;
        }

        var v = AppService.CoreVersion;

        // 0.0.0 is the placeholder used before the first migration writes a real version.
        if (AppConstants.InitialSemVersion.Equals(v))
        {
            return Dev;
        }

        return string.IsNullOrEmpty(v.Prerelease) ? Stable : Beta;
    }
}
