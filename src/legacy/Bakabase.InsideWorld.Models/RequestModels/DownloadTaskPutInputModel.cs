using Bakabase.InsideWorld.Models.Constants;
using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;
using System;

namespace Bakabase.InsideWorld.Models.RequestModels;

public record DownloadTaskPutInputModel
{
    public long? Interval { get; set; }
    public int? StartPage { get; set; }
    public int? EndPage { get; set; }
    public string? Checkpoint { get; set; }
    public bool AutoRetry { get; set; }
}