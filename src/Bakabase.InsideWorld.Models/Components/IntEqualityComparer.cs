using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Models.Components
{
    public class IntEqualityComparer : IEqualityComparer<int>
    {
        public static IntEqualityComparer Default { get; } = new IntEqualityComparer();

        public bool Equals(int x, int y)
        {
            return x == y;
        }

        public int GetHashCode(int obj)
        {
            return obj;
        }
    }
}
