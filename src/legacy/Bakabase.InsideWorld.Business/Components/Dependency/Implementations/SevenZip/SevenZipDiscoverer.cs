using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Infrastructures.Components.App.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Dependency.Discovery;
using Microsoft.Extensions.Logging;

namespace Bakabase.InsideWorld.Business.Components.Dependency.Implementations.SevenZip
{
    public class SevenZipDiscoverer : ExecutableDiscoverer
    {
        public SevenZipDiscoverer(ILoggerFactory loggerFactory) : base(loggerFactory)
        {
        }

        // On macOS and Linux, the executable is named "7zz", on Windows it's "7z.exe"
        protected override HashSet<string> RequiredRelativeFileNamesWithoutExtensions { get; } =
            AppService.OsPlatform == OsPlatform.Windows
                ? new HashSet<string> { "7z" }
                : new HashSet<string> { "7zz" };

        protected override string RelativeFileNameWithoutExtensionForAcquiringVersion =>
            AppService.OsPlatform == OsPlatform.Windows ? "7z" : "7zz";

        protected override string ArgumentsForAcquiringVersion => "--help";

        protected override string ParseVersion(string output)
        {
            // Example output from 7z -version:
            // 7-Zip 23.01 (x64) : Copyright (c) 1999-2023 Igor Pavlov : 2023-06-20
            var lines = output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length > 0)
            {
                var firstLine = lines[0];
                // 匹配类似 17.05、24.08、9.20.1
                var match = Regex.Match(firstLine, @"\d+(\.\d+){1,2}");
                if (match.Success)
                {
                    return match.Value;
                }
            }

            throw new Exception("Empty output from 7z -version command");
        }
    }
}
