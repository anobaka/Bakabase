using System;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Dtos;

namespace Bakabase.InsideWorld.Models.RequestModels
{
    public class ResourceUpdateRequestModel
    {
        public PublisherDto[]? Publishers { get; set; }
        public string? Name { get; set; }
        public string[]? Originals { get; set; }
        public string? Series { get; set; }
        public DateTime? ReleaseDt { get; set; }
        public ResourceLanguage? Language { get; set; }
        public decimal? Rate { get; set; }
        public TagDto[]? Tags { get; set; }
        public string? Introduction { get; set; }
    }
}