using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.InsideWorld.Models.Constants.Aos;
using Bakabase.InsideWorld.Models.Extensions;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Extensions;
using Bootstrap.Models.ResponseModels;
using Microsoft.Extensions.Localization;
using NPOI.SS.Formula.Functions;

namespace Bakabase.InsideWorld.Business.Extensions
{
    public static class FileSystemOptionsExtensions
    {
        public static BaseResponse StandardizeAndValidate(this FileSystemOptions.FileMoverOptions? options,
            IStringLocalizer localizer)
        {
            if (options?.Targets != null)
            {
                var targetDirectories = options.Targets.Where(a => a.Path.IsNotEmpty())
                    .ToDictionary(a => a.Path, a => new DirectoryInfo(a.Path).FullName.StandardizePath());

                // duplicate targets
                {
                    var duplicateTargets = targetDirectories.Values.GroupBy(a => a).Where(a => a.Count() > 1)
                        .Select(a => a.Key).ToArray();
                    if (duplicateTargets.Any())
                    {
                        return BaseResponseBuilder.BuildBadRequest(SharedResource.FileMover_DuplicateTargetsAreFound)
                            .WithLocalization(localizer, string.Join(Environment.NewLine, duplicateTargets));
                    }
                }

                // standardize targets
                options.Targets = targetDirectories.Select(a => new FileSystemOptions.FileMoverOptions.Target
                {
                    Path = a.Value,
                    Sources = options.Targets.FirstOrDefault(b => b.Path == a.Key)!.Sources
                }).ToList();

                foreach (var target in options.Targets)
                {
                    var t = target.Path;
                    var ss = target.Sources;
                    var sourceDirs = ss.Where(a => a.IsNotEmpty())
                        .Select(a => new DirectoryInfo(a).FullName.StandardizePath()).ToList();
                    // standardize sources
                    target.Sources = sourceDirs;
                }

                var allSources = options.Targets.SelectMany(a => a.Sources).OrderBy(a => a).ToArray();

                var duplicateSources =
                    allSources.GroupBy(a => a).Where(a => a.Count() > 1).Select(a => a.Key).ToArray();
                if (duplicateSources.Any())
                {
                    return BaseResponseBuilder.BuildBadRequest(localizer[
                        SharedResource.FileMover_DuplicateSourcesAreFound,
                        string.Join(Environment.NewLine, duplicateSources)]);
                }

                // parent-child relationship sources
                var noRelationshipSources = new List<string>();
                var subSources = new List<string>();
                foreach (var source in allSources)
                {
                    if (noRelationshipSources.All(a => !source.StartsWith(a) || a != source))
                    {
                        noRelationshipSources.Add(source);
                    }
                    else
                    {
                        subSources.Add(source);
                    }
                }

                if (subSources.Any())
                {
                    return BaseResponseBuilder.BuildBadRequest(localizer[
                        SharedResource.FileMover_SourcesCantBeSubOfSources,
                        string.Join(Environment.NewLine, subSources)]);
                }

                // source includes target
                var targetsUnderSources = new List<string>();
                foreach (var source in allSources)
                {
                    foreach (var target in options.Targets)
                    {
                        if (target.Path.StartsWith(source))
                        {
                            targetsUnderSources.Add(target.Path);
                        }
                    }
                }

                if (targetsUnderSources.Any())
                {
                    return BaseResponseBuilder.BuildBadRequest(localizer[
                        SharedResource.FileMover_TargetsCantBeSubOfSources,
                        string.Join(Environment.NewLine, targetsUnderSources)]);
                }
            }

            return BaseResponseBuilder.Ok;
        }
    }
}