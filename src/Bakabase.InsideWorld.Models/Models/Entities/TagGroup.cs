using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.InsideWorld.Models.Models.Entities
{
    public class TagGroup
    {
        public int Id { get; set; }
        [Required]
        public string Name { get; set; }
        public int Order { get; set; }
    }
}