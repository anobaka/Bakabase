using Bakabase.InsideWorld.Models.Constants;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Bakabase.InsideWorld.Models.RequestModels;

public record ExHentaiDownloadTaskAddInputModel
{
    [Required] public ExHentaiDownloadTaskType Type { get; set; }
    [Required] public string Link { get; set; } = null!;
}