using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Models.ResponseModels
{
    public class EverythingExtractionStatus
    {
        public bool Running { get; set; }
        public string Current { get; set; }
        public int DoneCount { get; set; }
        public int FailedCount { get; set; }
        public int TotalCount { get; set; }
        public int Percent => TotalCount == 0 ? 0 : (DoneCount + FailedCount) * 100 / TotalCount;
        public DateTime StartDt { get; set; }
        /// <summary>
        /// Fullname - Exception.Message
        /// </summary>
        public List<Failure> Failures { get; set; } = new();

        public class Failure
        {
            public string[] FullnameList { get; set; }
            public string Error { get; set; }
        }
    }
}