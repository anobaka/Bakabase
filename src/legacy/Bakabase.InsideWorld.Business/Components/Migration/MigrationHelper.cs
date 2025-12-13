using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Input;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Components.Resource.Components.PlayableFileSelector.Infrastructures;
using Bakabase.InsideWorld.Business.Extensions;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bootstrap.Extensions;
using DotNext.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bakabase.InsideWorld.Business.Components.Migration;

public class MigrationHelper(
    ICategoryService categoryService,
    IMediaLibraryService mediaLibraryService,
    IMediaLibraryTemplateService templateService,
    IMediaLibraryV2Service mediaLibraryV2Service,
    IOptions<EnhancerOptions> enhancerOptions,
    IResourceService resourceService,
    BakabaseDbContext dbCtx,
    ILogger<MigrationHelper> logger)
{
    public async Task MigrateCategoriesMediaLibrariesAndResources()
    {
        var changelog = string.Empty;

        // 2) Category & Media Library migration → MediaLibraryTemplate + MediaLibraryV2
        var categories = await categoryService.GetAll(null,
            CategoryAdditionalItem.CustomProperties |
            CategoryAdditionalItem.EnhancerOptions);
        var categoryMap = categories.ToDictionary(c => c.Id, c => c);

        var legacyLibraries = await mediaLibraryService.GetAll();

        // Map: (legacyMediaLibraryId, pathConfigIndex) -> (newTemplateId, newMediaLibraryV2Id)
        var migrationMap = new Dictionary<(int MlId, int PcIdx), (int TemplateId, int MlV2Id)>();

        foreach (var legacyMl in legacyLibraries)
        {
            if (!categoryMap.TryGetValue(legacyMl.CategoryId, out var category))
            {
                continue;
            }

            var pathConfigurations = legacyMl.PathConfigurations ?? [];
            for (var pcIdx = 0; pcIdx < pathConfigurations.Count; pcIdx++)
            {
                var pc = pathConfigurations[pcIdx];
                var path = pc.Path?.StandardizePath();
                var idxText = pcIdx.ToString();
                var name = $"{category.Name}-{legacyMl.Name}-{path}-{idxText}";

                // 2.1 Create MediaLibraryTemplate (idempotent)
                var existingTemplateDb = await dbCtx.MediaLibraryTemplates.FirstOrDefaultAsync(x => x.Name == name);
                MediaLibraryTemplate template;
                if (existingTemplateDb != null)
                {
                    template = existingTemplateDb.ToDomainModel();
                    logger.LogInformation($"Template exists, skip creation: #{template.Id} {template.Name}");
                }
                else
                {
                    var pfs = await categoryService.GetFirstComponent<IPlayableFileSelector>(category.Id,
                        ComponentType.PlayableFileSelector);
                    template = await templateService.Add(new MediaLibraryTemplateAddInputModel(name));

                    // Initialize template fields from legacy v1 definitions
                    template.InitFromMediaLibraryV1(legacyMl, pcIdx, category, pfs.Data, enhancerOptions);
                    await templateService.Put(template.Id, template);
                    logger.LogInformation($"Created template #{template.Id} {template.Name}");
                }

                // 2.2 Create MediaLibraryV2 (idempotent) and link to template
                var paths = new List<string>();
                if (path.IsNotEmpty())
                {
                    paths.Add(path!);
                }

                var existingMlV2 = (await mediaLibraryV2Service.GetAll(x => x.Name == name)).FirstOrDefault();
                int mlV2Id;
                if (existingMlV2 != null)
                {
                    mlV2Id = existingMlV2.Id;
                    logger.LogInformation($"MediaLibraryV2 exists, skip creation: #{mlV2Id} {name}");
                }
                else
                {
                    var addModel = new MediaLibraryV2AddOrPutInputModel(name, paths, category.Color);
                    existingMlV2 = await mediaLibraryV2Service.Add(addModel);
                    mlV2Id = existingMlV2.Id;
                    logger.LogInformation($"Created MLv2 #{mlV2Id} {name}");
                }

                // Link template to mlv2 if not already
                if (existingMlV2.TemplateId != template.Id)
                {
                    await mediaLibraryV2Service.Patch(mlV2Id, new MediaLibraryV2PatchInputModel
                    {
                        TemplateId = template.Id
                    });
                    logger.LogInformation($"Linked template #{template.Id} to MLv2 #{mlV2Id}");
                }

                migrationMap[(legacyMl.Id, pcIdx)] = (template.Id, mlV2Id);
                logger.LogInformation(
                    $"Prepared template #{template.Id} and MLv2 #{mlV2Id} for legacy ML #{legacyMl.Id} PC[{pcIdx}] {path}");
            }
        }

        // 3) Reassign resources: legacy Category/MediaLibrary -> new MediaLibraryV2
        // Rule: categoryId = 0, mediaLibraryId = newMlV2Id (matched by path prefix to PC.Path)
        if (migrationMap.Any())
        {
            // Preload legacy MLs and their PCs for path matching
            var legacyMlPcMap = legacyLibraries.ToDictionary(
                ml => ml.Id,
                ml => (ml.PathConfigurations ?? []).Select((pc, i) => (pc, i)).ToList());

            var legacyResources = await resourceService.GetAll(r => r.CategoryId > 0);

            var changed = 0;
            var mlIdResourceIdsMap = new Dictionary<int, List<int>>();
            foreach (var r in legacyResources)
            {
                // Match legacy ML and then select best PC by longest prefix
                if (!legacyMlPcMap.TryGetValue(r.MediaLibraryId, out var pcs))
                {
                    continue;
                }

                var resourcePath = r.Path.StandardizePath()!;
                var candidates = pcs
                    .Select(d =>
                    {
                        var path = d.pc.Path.StandardizePath()! + InternalOptions.DirSeparator;
                        return new
                        {
                            d.i,
                            Path = path,
                            Len = path.Length
                        };
                    })
                    .Where(x => x.Path.IsNotEmpty() &&
                                resourcePath.StartsWith(x.Path!, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(x => x.Len)
                    .ToList();

                // Fallback to first PC if none matched
                var selectedPcIdx = candidates.FirstOrDefault()?.i ?? (pcs.Count > 0 ? 0 : -1);
                if (selectedPcIdx < 0)
                {
                    continue;
                }

                if (migrationMap.TryGetValue((r.MediaLibraryId, selectedPcIdx), out var tuple))
                {
                    mlIdResourceIdsMap.GetOrAdd(tuple.MlV2Id, i => []).Add(r.Id);
                    changed++;
                }

                r.CategoryId = 0;
            }

            await resourceService.AddOrPutRange(legacyResources);

            if (changed > 0)
            {
                foreach (var (mlId, rIds) in mlIdResourceIdsMap)
                {
                    await resourceService.ChangeMediaLibrary(rIds.ToArray(), mlId);
                }

                logger.LogInformation($"Reassigned {changed} resources to new MediaLibraryV2 instances.");
            }
        }
    }
}