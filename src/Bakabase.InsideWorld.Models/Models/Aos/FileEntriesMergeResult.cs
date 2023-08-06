using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Models.Models.Aos
{
    public record FileEntriesMergeResult(string RootPath, string[] CurrentNames,
        Dictionary<string, string[]> MergeResult)
    {
        public string RootPath { get; set; } = RootPath;

        /// <summary>
        /// Filename only
        /// </summary>
        public string[] CurrentNames { get; set; } = CurrentNames;

        /// <summary>
        /// Directory name - file names
        /// </summary>
        public Dictionary<string, string[]> MergeResult { get; set; } = MergeResult;
    }
}