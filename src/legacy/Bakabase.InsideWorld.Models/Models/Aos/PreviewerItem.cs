using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.InsideWorld.Models.Models.Aos
{
    public class PreviewerItem
    {
        public string FilePath { get; set; } = null!;
        public MediaType Type { get; set; }
        /// <summary>
        /// 1 for image, {duration} for video, 0 for others
        /// </summary>
        public int Duration { get; set; }
    }
}