using Bakabase.InsideWorld.Models.Models.Aos;
using Bakabase.InsideWorld.Models.Models.Dtos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;
using System.ComponentModel.DataAnnotations;

namespace Bakabase.InsideWorld.Business.Models.Domain
{
    public record MediaLibrary
    {
        public int Id { get; set; }
        [Required] public string Name { get; set; } = string.Empty;
        [Required] public int CategoryId { get; set; }
        public int Order { get; set; }
        public int ResourceCount { get; set; }
        public Dictionary<string, MediaLibraryFileSystemInformation>? FileSystemInformation { get; set; }
        public Category? Category { get; set; }
        public List<PathConfiguration>? PathConfigurations { get; set; }

        public static MediaLibrary CreateDefault(string name, int categoryId, params string[]? rootPaths)
        {
            return new MediaLibrary
            {
                Name = name,
                CategoryId = categoryId,
                Order = 0,
                ResourceCount = 0,
                PathConfigurations = rootPaths?.Select(PathConfiguration.CreateDefault).ToList()
            };
        }
    }
}