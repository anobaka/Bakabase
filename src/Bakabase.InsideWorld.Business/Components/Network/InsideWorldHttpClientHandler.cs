using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Business.Components.Network
{
    public class InsideWorldHttpClientHandler: HttpClientHandler
    {
        public InsideWorldHttpClientHandler(InsideWorldWebProxy webProxy)
        {
            Proxy = webProxy;
        }
    }
}
