using System;

namespace Bakabase.InsideWorld.Models.RequestModels
{
    public class FileDecompressRequestModel
    {
        public string[] Paths { get; set; } = Array.Empty<string>();
        public string? Password { get; set; }
    }
}
