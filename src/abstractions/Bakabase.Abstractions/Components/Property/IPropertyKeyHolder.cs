using Bakabase.Abstractions.Models.Domain.Constants;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.Abstractions.Components.Property
{
    public interface IPropertyKeyHolder
    {
        public PropertyPool PropertyPool { get; set; }
        public int PropertyId { get; set; }
    }
}
