using System.Linq;
using System.Text.RegularExpressions;
using Semver;

namespace Bakabase.InsideWorld.Business.Components.Dependency
{
    /// <summary>
    /// The one place that decides whether a dependent component's latest version is newer than
    /// the installed one. The update prompt and the installer both ask it, so the settings page
    /// never offers an update the installer would then skip, or the other way round.
    /// </summary>
    public static partial class ComponentVersionComparison
    {
        /// <summary>
        /// Parses a component version string tolerantly. Some components report
        /// 4-segment versions (e.g. Locale Emulator's "2.5.0.1") that SemVer
        /// cannot parse; those are truncated to the first three segments. Build
        /// names SemVer rejects (<c>6.0-essentials_build-www.gyan.dev</c>,
        /// <c>n7.1.1</c>) are read by their leading release core, which needs at
        /// least one dot, so git-date and nightly builds (<c>2021-01-31-git-…</c>,
        /// <c>N-12345-g…</c>) stay unparsable.
        /// Returns null when the value still cannot be parsed.
        /// </summary>
        public static SemVersion? TryParse(string? version)
        {
            if (string.IsNullOrWhiteSpace(version))
            {
                return null;
            }

            if (SemVersion.TryParse(version, SemVersionStyles.Any, out var semVersion))
            {
                return semVersion;
            }

            var core = ReleaseCore().Match(version.Trim());
            if (core.Success)
            {
                var truncatedCore = string.Join('.', core.Groups["core"].Value.Split('.').Take(3));
                if (SemVersion.TryParse(truncatedCore, SemVersionStyles.Any, out semVersion))
                {
                    return semVersion;
                }
            }

            var segments = version.Split('.');
            if (segments.Length > 3)
            {
                var truncated = string.Join('.', segments.Take(3));
                if (SemVersion.TryParse(truncated, SemVersionStyles.Any, out semVersion))
                {
                    return semVersion;
                }
            }

            return null;
        }

        /// <summary>
        /// Whether <paramref name="latestVersion"/> is newer than <paramref name="installedVersion"/>.
        /// <list type="bullet">
        /// <item>An unknown latest version ("N/A", null, unparsable) never offers an update.</item>
        /// <item>Nothing installed and a known latest version: an update (the install) is offered.</item>
        /// <item>An installed version that cannot be parsed (e.g. a git-date ffmpeg build) is not nagged about.</item>
        /// </list>
        /// Only release cores are compared: discovered versions carry build suffixes such as
        /// <c>7.1.1-tessus</c> or <c>6.1-static</c>, which SemVer would read as prereleases sorting
        /// below <c>7.1.1</c>/<c>6.1</c> and so would offer the same version forever.
        /// </summary>
        public static bool IsUpdateAvailable(string? installedVersion, string? latestVersion)
        {
            var latest = TryParse(latestVersion);
            if (latest == null)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(installedVersion))
            {
                return true;
            }

            var installed = TryParse(installedVersion);
            if (installed == null)
            {
                return false;
            }

            return latest.WithoutPrereleaseOrMetadata()
                .ComparePrecedenceTo(installed.WithoutPrereleaseOrMetadata()) > 0;
        }

        [GeneratedRegex(@"^[vVnN]?(?<core>\d+(?:\.\d+){1,3})(?=$|[-+_ ~])")]
        private static partial Regex ReleaseCore();
    }
}
