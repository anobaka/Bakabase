using System.Linq.Expressions;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;

namespace Bakabase.Abstractions.Services;

public interface IPathRuleService
{
    /// <summary>
    /// Get all path rules
    /// </summary>
    Task<List<PathRule>> GetAll(Expression<Func<PathRuleDbModel, bool>>? filter = null);

    /// <summary>
    /// Get a path rule by ID
    /// </summary>
    Task<PathRule?> Get(int id);

    /// <summary>
    /// Get a path rule by path
    /// </summary>
    Task<PathRule?> GetByPath(string path);

    /// <summary>
    /// Get applicable rules for a given path (rules whose Path is a prefix of the given path)
    /// </summary>
    Task<List<PathRule>> GetApplicableRules(string path);

    /// <summary>
    /// Add a new path rule
    /// </summary>
    Task<PathRule> Add(PathRule rule);

    /// <summary>
    /// Update an existing path rule
    /// </summary>
    Task Update(PathRule rule);

    /// <summary>
    /// Delete a path rule by ID
    /// </summary>
    Task Delete(int id);

    /// <summary>
    /// Copy rule configuration from source path
    /// </summary>
    Task<string> CopyRuleConfig(string sourcePath);

    /// <summary>
    /// Apply rule configuration to multiple target paths
    /// </summary>
    Task ApplyRuleConfig(string marksJson, List<string> targetPaths);

    /// <summary>
    /// Preview resources that would be matched by a rule
    /// </summary>
    Task<List<string>> PreviewMatchedPaths(PathRule rule);
}
