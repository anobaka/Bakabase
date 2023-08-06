using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.InsideWorld.Models.Models.Entities
{
    public class Tag
    {
        public int Id { get; set; }
        [Required]
        public string Name { get; set; }
        public string? Color { get; set; }
        public int GroupId { get; set; }
        public int Order { get; set; }
        public int Source { get; set; }
    }
}