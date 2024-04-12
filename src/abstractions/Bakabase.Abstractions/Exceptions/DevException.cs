using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.Abstractions.Exceptions
{
    public class DevException : Exception
    {
        public DevException(string message) : base(message)
        {
        }

        public DevException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
