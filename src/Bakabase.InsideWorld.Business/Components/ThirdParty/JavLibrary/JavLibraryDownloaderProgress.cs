using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bootstrap.Components.Tasks.Progressor.Abstractions.Models;

namespace Bakabase.InsideWorld.Business.Components.ThirdParty.JavLibrary
{
    public class JavLibraryDownloaderProgress : ProgressorProgress
    {
        /// <summary>
        /// Url - Status
        /// </summary>
        public Dictionary<string, bool?> Results { get; set; }

        public int DoneCount { get; set; }
        public int TotalCount { get; set; }
        public int FailedCount { get; set; }

        public override double Percentage =>
            TotalCount == 0 ? 0 : double.Parse(((double)(DoneCount + FailedCount) / TotalCount * 100).ToString("F2"));
    }
}