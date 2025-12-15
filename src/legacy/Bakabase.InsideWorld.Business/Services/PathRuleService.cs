using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bootstrap.Components.DependencyInjection;
using Bootstrap.Components.Orm;
using Bootstrap.Components.Orm.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;

namespace Bakabase.InsideWorld.Business.Services;

public class PathRuleService<TDbContext>(
    FullMemoryCacheResourceService<TDbContext, PathRuleDbModel, int> orm,
    IExtensionGroupService extensionGroupService,
    IServiceProvider serviceProvider
) : ScopedService(serviceProvider), IPathRuleService where TDbContext : DbContext
{
    public async Task<List<PathRule>> GetAll(Expression<Func<PathRuleDbModel, bool>>? filter = null)
    {
        var dbModels = filter != null
            ? await orm.GetAll(filter)
            : await orm.GetAll();

        return dbModels.Select(d => d.ToDomainModel()).ToList();
    }

    public async Task<PathRule?> Get(int id)
    {
        var dbModel = await orm.GetByKey(id);
        return dbModel?.ToDomainModel();
    }

    public async Task<PathRule?> GetByPath(string path)
    {
        var normalizedPath = path.StandardizePath();
        var allRules = await orm.GetAll(r => r.Path == normalizedPath);
        var dbModel = allRules.FirstOrDefault();
        return dbModel?.ToDomainModel();
    }

    public async Task<List<PathRule>> GetApplicableRules(string path)
    {
        var normalizedPath = path.StandardizePath()!;
        var allRules = await orm.GetAll();

        // Find rules whose Path is a prefix of the given path
        var applicableRules = allRules
            .Where(r => normalizedPath.StartsWith(r.Path, StringComparison.OrdinalIgnoreCase) ||
                        r.Path.Equals(normalizedPath, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(r => r.Path.Length) // More specific paths first
            .Select(r => r.ToDomainModel())
            .ToList();

        return applicableRules;
    }

    public async Task<PathRule> Add(PathRule rule)
    {
        rule.Path = rule.Path.StandardizePath()!;
        rule.CreateDt = DateTime.UtcNow;
        rule.UpdateDt = DateTime.UtcNow;

        var dbModel = rule.ToDbModel();
        await orm.Add(dbModel);
        orm.DbContext.Detach(dbModel);
        rule.Id = dbModel.Id;
        return rule;
    }

    public async Task Update(PathRule rule)
    {
        rule.Path = rule.Path.StandardizePath()!;
        rule.UpdateDt = DateTime.UtcNow;
        await orm.Update(rule.ToDbModel());
    }

    public async Task Delete(int id)
    {
        await orm.RemoveByKey(id);
    }

    public async Task<string> CopyRuleConfig(string sourcePath)
    {
        var rule = await GetByPath(sourcePath);
        if (rule == null)
        {
            throw new InvalidOperationException($"No rule found for path: {sourcePath}");
        }

        return JsonConvert.SerializeObject(rule.Marks);
    }

    public async Task ApplyRuleConfig(string marksJson, List<string> targetPaths)
    {
        var marks = JsonConvert.DeserializeObject<List<PathMark>>(marksJson) ?? new List<PathMark>();
        var now = DateTime.UtcNow;

        foreach (var targetPath in targetPaths)
        {
            var normalizedPath = targetPath.StandardizePath()!;
            var existingRule = await GetByPath(normalizedPath);

            if (existingRule != null)
            {
                existingRule.Marks = marks;
                existingRule.UpdateDt = now;
                await Update(existingRule);
            }
            else
            {
                var newRule = new PathRule
                {
                    Path = normalizedPath,
                    Marks = marks,
                    CreateDt = now,
                    UpdateDt = now
                };
                await Add(newRule);
            }
        }
    }

    public async Task<List<string>> PreviewMatchedPaths(PathRule rule)
    {
        var matchedPaths = new List<string>();

        if (!Directory.Exists(rule.Path))
        {
            return matchedPaths;
        }

        // Find Resource marks to determine what should be matched
        var resourceMarks = rule.Marks
            .Where(m => m.Type == PathMarkType.Resource)
            .ToList();

        foreach (var mark in resourceMarks)
        {
            var config = JsonConvert.DeserializeObject<ResourceMarkConfig>(mark.ConfigJson);
            if (config == null) continue;

            var paths = await GetMatchingPathsForResourceMark(rule.Path, config);
            matchedPaths.AddRange(paths);
        }

        return matchedPaths.Distinct().ToList();
    }

    private async Task<List<string>> GetAllExtensionsFromConfig(ResourceMarkConfig config)
    {
        var allExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Add direct extensions
        if (config.Extensions != null)
        {
            foreach (var ext in config.Extensions)
            {
                allExtensions.Add(ext);
            }
        }

        // Add extensions from extension groups
        if (config.ExtensionGroupIds != null && config.ExtensionGroupIds.Count > 0)
        {
            var allGroups = await extensionGroupService.GetAll();
            var matchingGroups = allGroups.Where(g => config.ExtensionGroupIds.Contains(g.Id));

            foreach (var group in matchingGroups)
            {
                if (group.Extensions != null)
                {
                    foreach (var ext in group.Extensions)
                    {
                        allExtensions.Add(ext);
                    }
                }
            }
        }

        return allExtensions.ToList();
    }

    private async Task<List<string>> GetMatchingPathsForResourceMark(string rootPath, ResourceMarkConfig config)
    {
        var matchedPaths = new List<string>();

        try
        {
            // Get all extensions (direct + from groups)
            var allExtensions = await GetAllExtensionsFromConfig(config);
            var extensionsToUse = allExtensions.Count > 0 ? allExtensions : null;

            if (config.MatchMode == PathMatchMode.Layer)
            {
                if (config.Layer == null) return matchedPaths;

                var layer = config.Layer.Value;
                if (layer == -1)
                {
                    // All layers - recursively get all directories/files
                    var entries = GetAllEntries(rootPath, config.FsTypeFilter, extensionsToUse);
                    matchedPaths.AddRange(entries);
                }
                else
                {
                    // Specific layer
                    var entries = GetEntriesAtLayer(rootPath, layer, config.FsTypeFilter, extensionsToUse);
                    matchedPaths.AddRange(entries);
                }
            }
            else if (config.MatchMode == PathMatchMode.Regex && !string.IsNullOrEmpty(config.Regex))
            {
                var regex = new Regex(config.Regex, RegexOptions.IgnoreCase);
                var entries = GetAllEntries(rootPath, config.FsTypeFilter, extensionsToUse);

                foreach (var entry in entries)
                {
                    var relativePath = entry.Substring(rootPath.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    if (regex.IsMatch(relativePath))
                    {
                        matchedPaths.Add(entry);
                    }
                }
            }
        }
        catch (Exception)
        {
            // Ignore access errors
        }

        return matchedPaths;
    }

    private List<string> GetAllEntries(string rootPath, PathFilterFsType? fsTypeFilter, List<string>? extensions)
    {
        var entries = new List<string>();

        try
        {
            if (fsTypeFilter == null || fsTypeFilter == PathFilterFsType.Directory)
            {
                entries.AddRange(Directory.GetDirectories(rootPath, "*", SearchOption.AllDirectories));
            }

            if (fsTypeFilter == null || fsTypeFilter == PathFilterFsType.File)
            {
                var files = Directory.GetFiles(rootPath, "*", SearchOption.AllDirectories);
                if (extensions != null && extensions.Count > 0)
                {
                    files = files.Where(f => extensions.Any(ext =>
                        f.EndsWith(ext, StringComparison.OrdinalIgnoreCase))).ToArray();
                }
                entries.AddRange(files);
            }
        }
        catch (Exception)
        {
            // Ignore access errors
        }

        return entries.Select(x => x.StandardizePath()!).ToList();
    }

    private List<string> GetEntriesAtLayer(string rootPath, int layer, PathFilterFsType? fsTypeFilter, List<string>? extensions)
    {
        var entries = new List<string>();

        try
        {
            var currentPaths = new List<string> { rootPath };

            for (int i = 1; i <= layer; i++)
            {
                var nextPaths = new List<string>();
                foreach (var path in currentPaths)
                {
                    try
                    {
                        nextPaths.AddRange(Directory.GetDirectories(path));
                        if (i == layer && (fsTypeFilter == null || fsTypeFilter == PathFilterFsType.File))
                        {
                            var files = Directory.GetFiles(path);
                            if (extensions != null && extensions.Count > 0)
                            {
                                files = files.Where(f => extensions.Any(ext =>
                                    f.EndsWith(ext, StringComparison.OrdinalIgnoreCase))).ToArray();
                            }
                            nextPaths.AddRange(files);
                        }
                    }
                    catch (Exception)
                    {
                        // Ignore access errors
                    }
                }
                currentPaths = nextPaths;
            }

            if (fsTypeFilter == PathFilterFsType.Directory)
            {
                entries.AddRange(currentPaths.Where(Directory.Exists));
            }
            else if (fsTypeFilter == PathFilterFsType.File)
            {
                entries.AddRange(currentPaths.Where(File.Exists));
            }
            else
            {
                entries.AddRange(currentPaths);
            }
        }
        catch (Exception)
        {
            // Ignore access errors
        }

        return entries.Select(x => x.StandardizePath()!).ToList(); ;
    }
}
