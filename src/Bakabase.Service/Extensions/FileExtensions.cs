using System.IO;
using System.Linq;
using Bakabase.Abstractions.Extensions;
using Bakabase.InsideWorld.Business.Components.Compression;
using Bakabase.InsideWorld.Business.Extensions;
using Bakabase.Service.Models.View;
using Bakabase.Service.Models.View.Constants;

namespace Bakabase.Service.Extensions;

public static class FileExtensions
{
    public static CompressedFileDetectionResultViewModel ToViewModel(this CompressedFileGroup group)
    {
        return new CompressedFileDetectionResultViewModel
        {
            Key = group.Files[0],
            Status = CompressedFileDetectionResultStatus.Init,
            Directory = Path.GetDirectoryName(group.Files[0]).StandardizePath()!,
            GroupKey = group.KeyName,
            Files = group.Files.Select(p => Path.GetFileName(p)!).ToArray(),
            DecompressToDirName = group.KeyName,
            PasswordCandidates = group.Files[0].GetPasswordsFromPath(),
            FileSizes = group.FileSizes.ToArray()
        };
    }
}