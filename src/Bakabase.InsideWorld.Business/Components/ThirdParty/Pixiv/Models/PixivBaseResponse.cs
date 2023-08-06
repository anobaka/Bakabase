using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Business.Components.ThirdParty.Pixiv.Models
{
    public class PixivBaseResponse
    {
        public bool Error { get; set; }
        public string Message { get; set; }
    }

    public class PixivBaseResponse<T> : PixivBaseResponse
    {
        public T Body { get; set; }
    }
}