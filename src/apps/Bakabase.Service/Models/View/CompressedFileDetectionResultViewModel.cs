using System;
using System.Collections.Generic;
using Bakabase.Service.Models.Input;
using Bakabase.Service.Models.View.Constants;
using Bootstrap.Components.Doc.Swagger;

namespace Bakabase.Service.Models.View;

[SwaggerCustomModel]
public record CompressedFileDetectionResultViewModel
{
    /// <summary>
    /// Use first file path in compressed file groups as identifier key
    /// </summary>
    public string Key { get; set; } = string.Empty;
    public CompressedFileDetectionResultStatus? Status { get; set; }
    public string? Message { get; set; }
    public string? Directory { get; set; }
    public string? GroupKey { get; set; }
    public string[]? Files { get; set; }
    public long[]? FileSizes { get; set; }
    public string? Password { get; set; }
    public string[]? WrongPasswords { get; set; }
    public string[]? PasswordCandidates { get; set; }
    public string? DecompressToDirName { get; set; }
    public SampleGroup[]? ContentSampleGroups { get; set; }
    public record SampleGroup
    {
        public bool IsFile { get; set; }
        public int Count { get; set; }
        public string[] Samples { get; set; } = [];
    }
}