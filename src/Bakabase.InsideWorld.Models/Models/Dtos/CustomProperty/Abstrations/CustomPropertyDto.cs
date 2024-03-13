using Bakabase.InsideWorld.Models.Constants;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Models.Models.Dtos.CustomProperty.Abstrations
{
    public abstract record CustomPropertyDto
    {
        public int Id { get; set; }
        public string Key { get; set; } = null!;
        public string DisplayName { get; set; } = null!;
        public CustomPropertyType Type { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public abstract record CustomPropertyDto<T> : CustomPropertyDto
    {
        public T? Configuration { get; set; }
    }
}
