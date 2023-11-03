using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Dependency.Discovery;
using Microsoft.Extensions.Logging;

namespace Bakabase.InsideWorld.Business.Components.Dependency.Implementations.Lux
{
    public class LuxDiscover : ExecutableDiscoverer
    {
        protected override HashSet<string> RequiredRelativeFileNamesWithoutExtensions { get; } =
            new HashSet<string>() {"lux"};

        protected override string RelativeFileNameWithoutExtensionForAcquiringVersion => "lux";
        protected override string ArgumentsForAcquiringVersion => "-v";

        public LuxDiscover(ILoggerFactory loggerFactory) : base(loggerFactory)
        {
        }

        protected override string ParseVersion(string output)
        {
            //
            // lux: version v0.16.0, A fast and simple video downloader.
            //

            // lux returns \n instead of Environment.NewLine
            var core = output.Split('\n').Select(a => a.Trim()).ToArray()[1];
            var segments = core.Split(' ', ',').ToList();

            var versionIndex = segments.IndexOf("version");
            if (versionIndex == -1)
            {
                throw new Exception("'version' is not found in output");
            }

            if (segments.Count < versionIndex + 2)
            {
                throw new Exception("There is no version info after 'version'");
            }

            return segments[versionIndex + 1];
        }
    }
}