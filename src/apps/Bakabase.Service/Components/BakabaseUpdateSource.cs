using System;
using Bakabase.Infrastructures.Components.App.Upgrade.Abstractions;

namespace Bakabase.Service.Components
{
    /// <summary>
    /// Bakabase-specific implementation of <see cref="IAppUpdateSource"/>. Resolves the
    /// Velopack feed base URL with the precedence:
    /// <list type="number">
    ///   <item><c>BAKABASE_UPDATE_URL</c> environment variable (operator override; mirrors
    ///   the <c>BAKABASE_DATA_DIR</c> pattern).</item>
    ///   <item>Hardcoded Bakabase production CDN.</item>
    /// </list>
    /// The deployment-specific URL lives here rather than in
    /// <c>Bakabase.Infrastructures</c> so that library can stay generic across forks /
    /// downstream apps.
    /// </summary>
    public sealed class BakabaseUpdateSource : IAppUpdateSource
    {
        public const string EnvVarName = "BAKABASE_UPDATE_URL";
        public const string DefaultBaseUrl = "https://cdn-public.anobaka.com/app/bakabase/releases/";

        public string GetBaseUrl()
        {
            var envOverride = Environment.GetEnvironmentVariable(EnvVarName);
            if (!string.IsNullOrWhiteSpace(envOverride)) return envOverride.Trim();
            return DefaultBaseUrl;
        }
    }
}
