using System.Text.RegularExpressions;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Modules.MediaLibraryTemplate.Abstractions.Components.PathLocator;
using Bakabase.Modules.MediaLibraryTemplate.Abstractions.Services;
using Bootstrap.Components.Orm;
using Bootstrap.Extensions;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Modules.MediaLibraryTemplate.Services;

public abstract class AbstractMediaLibraryTemplateService<TDbContext>(
    FullMemoryCacheResourceService<TDbContext, Abstractions.Models.MediaLibraryTemplate, int> orm)
    : IMediaLibraryTemplateService
    where TDbContext : DbContext
{
    public async Task GeneratePreview(int id)
    {
        var template = await orm.GetByKey(id);
        template.SamplePaths = template.SamplePaths?.Distinct().ToList();

        var rootPath = template.SamplePaths?.FirstOrDefault().StandardizePath();
        if (rootPath.IsNullOrEmpty())
        {
            throw new ArgumentNullException(nameof(template.SamplePaths));
        }

        var subPaths = template.SamplePaths!.Skip(1).Select(x => x.StandardizePath()!).ToList();
        if (!subPaths.Any())
        {
            throw new ArgumentNullException(nameof(subPaths));
        }

        if (template.ResourceFilters == null)
        {
            throw new ArgumentNullException(nameof(template.ResourceFilters));
        }

        var subRelativePaths = subPaths.Select(x => x.Replace(rootPath, null).Trim(InternalOptions.DirSeparator)).ToList();
        var subPathSegments = subRelativePaths.Select(x => x.Split(InternalOptions.DirSeparator)).ToList();
        var resourcePathSegments = new List<string[]>();
        foreach (var rf in template.ResourceFilters)
        {
            switch (rf.Positioner)
            {
                case PathPositioner.Layer:
                {
                    if (rf.Layer.HasValue)
                    {
                        foreach (var segments in subPathSegments)
                        {
                            var len = rf.Layer.Value;
                            if (len >= 0 && len < segments.Length)
                            {
                                resourcePathSegments.Add(segments.Take(len).ToArray());
                            }
                        }
                    }

                    break;
                }
                case PathPositioner.Regex:
                {
                    if (rf.Regex.IsNotEmpty())
                    {
                        for (var index = 0; index < subRelativePaths.Count; index++)
                        {
                            var relativePath = subRelativePaths[index];
                            var match = Regex.Match(relativePath, rf.Regex);
                            if (match.Success)
                            {
                                var len = match.Value.Split(InternalOptions.DirSeparator,
                                    StringSplitOptions.RemoveEmptyEntries).Length;
                                resourcePathSegments.Add(subPathSegments[index].Take(len).ToArray());
                            }
                        }
                    }

                    break;
                }
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        // properties
        // playable file selectors
        // enhancements
        // displaynametemplate
    }

    public async Task Add(Abstractions.Models.MediaLibraryTemplate template)
    {
        await orm.Add(template);
    }

    public async Task Put(int id, Abstractions.Models.MediaLibraryTemplate template)
    {
        template.Id = id;
        await orm.Update(template);
    }

    public async Task Delete(int id)
    {
        await orm.RemoveByKey(id);
    }

    public Task<string> ExportToText(int id)
    {
        throw new NotImplementedException();
    }

    public Task<byte[]> ExportToPng(int id)
    {
        throw new NotImplementedException();
    }
}