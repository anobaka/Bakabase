using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Newtonsoft.Json;

namespace Bakabase.Tests.Utils;

/// <summary>
/// Generates random PathMarks for comprehensive testing coverage.
///
/// Coverage matrix:
/// - PathMarkType: Resource, Property, MediaLibrary
/// - PathMatchMode: Layer, Regex
/// - PathFilterFsType: File, Directory (Resource only)
/// - PathMarkApplyScope: MatchedOnly, MatchedAndSubdirectories
/// - PropertyValueType: Fixed, Dynamic (Property/MediaLibrary only)
/// - PropertyPool: Internal, Reserved, Custom (Property only)
/// </summary>
public static class PathMarkGenerator
{
    private static readonly Random Random = new();

    #region Resource Mark Generators

    /// <summary>
    /// 生成所有 Resource Mark 配置组合
    /// </summary>
    public static IEnumerable<(PathMark Mark, ResourceMarkConfig Config, string Description)> GenerateAllResourceMarkCombinations(
        string basePath)
    {
        var matchModes = new[] { PathMatchMode.Layer, PathMatchMode.Regex };
        var fsTypes = new PathFilterFsType?[] { null, PathFilterFsType.File, PathFilterFsType.Directory };
        var applyScopes = new[] { PathMarkApplyScope.MatchedOnly, PathMarkApplyScope.MatchedAndSubdirectories };
        var layers = new[] { -3, -2, -1, 0, 1, 2, 3, 4, 5 };
        var regexPatterns = new[] { @".*\.mp4$", @"^Episode\d+$", @"^Series_.*$" };

        var priority = 1;

        foreach (var matchMode in matchModes)
        {
            foreach (var fsType in fsTypes)
            {
                foreach (var applyScope in applyScopes)
                {
                    if (matchMode == PathMatchMode.Layer)
                    {
                        foreach (var layer in layers)
                        {
                            var config = new ResourceMarkConfig
                            {
                                MatchMode = matchMode,
                                Layer = layer,
                                FsTypeFilter = fsType,
                                ApplyScope = applyScope
                            };

                            var mark = new PathMark
                            {
                                Path = basePath,
                                Type = PathMarkType.Resource,
                                ConfigJson = JsonConvert.SerializeObject(config),
                                Priority = priority++
                            };

                            var desc = $"Resource_Layer{layer}_FsType{fsType?.ToString() ?? "All"}_Scope{applyScope}";
                            yield return (mark, config, desc);
                        }
                    }
                    else // Regex
                    {
                        foreach (var regex in regexPatterns)
                        {
                            var config = new ResourceMarkConfig
                            {
                                MatchMode = matchMode,
                                Regex = regex,
                                FsTypeFilter = fsType,
                                ApplyScope = applyScope
                            };

                            var mark = new PathMark
                            {
                                Path = basePath,
                                Type = PathMarkType.Resource,
                                ConfigJson = JsonConvert.SerializeObject(config),
                                Priority = priority++
                            };

                            var safeRegex = regex.Replace(".", "_").Replace("*", "x").Replace("$", "").Replace("^", "");
                            var desc = $"Resource_Regex_{safeRegex}_FsType{fsType?.ToString() ?? "All"}_Scope{applyScope}";
                            yield return (mark, config, desc);
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// 生成随机 Resource Mark
    /// </summary>
    public static (PathMark Mark, ResourceMarkConfig Config) GenerateRandomResourceMark(
        string basePath,
        PathMatchMode? matchMode = null,
        PathFilterFsType? fsType = null)
    {
        matchMode ??= Random.Next(2) == 0 ? PathMatchMode.Layer : PathMatchMode.Regex;

        var config = new ResourceMarkConfig
        {
            MatchMode = matchMode.Value,
            FsTypeFilter = fsType ?? (Random.Next(3) switch { 0 => null, 1 => PathFilterFsType.File, _ => PathFilterFsType.Directory }),
            ApplyScope = Random.Next(2) == 0 ? PathMarkApplyScope.MatchedOnly : PathMarkApplyScope.MatchedAndSubdirectories
        };

        if (matchMode == PathMatchMode.Layer)
        {
            config.Layer = Random.Next(-3, 6);
        }
        else
        {
            config.Regex = new[] { @".*\.mp4$", @"^Episode\d+$", @"^Series_.*$" }[Random.Next(3)];
        }

        var mark = new PathMark
        {
            Path = basePath,
            Type = PathMarkType.Resource,
            ConfigJson = JsonConvert.SerializeObject(config),
            Priority = Random.Next(1, 100)
        };

        return (mark, config);
    }

    #endregion

    #region Property Mark Generators

    /// <summary>
    /// 生成所有 Property Mark 配置组合
    /// </summary>
    public static IEnumerable<(PathMark Mark, PropertyMarkConfig Config, string Description)> GenerateAllPropertyMarkCombinations(
        string basePath,
        int customPropertyId)
    {
        var matchModes = new[] { PathMatchMode.Layer, PathMatchMode.Regex };
        var valueTypes = new[] { PropertyValueType.Fixed, PropertyValueType.Dynamic };
        var applyScopes = new[] { PathMarkApplyScope.MatchedOnly, PathMarkApplyScope.MatchedAndSubdirectories };
        var pools = new[] { PropertyPool.Custom, PropertyPool.Reserved };
        var layers = new[] { -3, -2, -1, 0, 1, 2, 3, 4, 5 };
        var valueLayers = new[] { -3, -2, -1, 0, 1, 2, 3, 4, 5 };
        var regexPatterns = new[] { @".+", @"^Series_(.+)$" };
        var valueRegexPatterns = new string?[] { null, @"(.+)" };

        var priority = 1;

        foreach (var matchMode in matchModes)
        {
            foreach (var valueType in valueTypes)
            {
                foreach (var applyScope in applyScopes)
                {
                    foreach (var pool in pools)
                    {
                        if (matchMode == PathMatchMode.Layer)
                        {
                            foreach (var layer in layers)
                            {
                                if (valueType == PropertyValueType.Fixed)
                                {
                                    var config = new PropertyMarkConfig
                                    {
                                        MatchMode = matchMode,
                                        Layer = layer,
                                        Pool = pool,
                                        PropertyId = pool == PropertyPool.Custom ? customPropertyId : (int)ReservedProperty.Rating,
                                        ValueType = valueType,
                                        FixedValue = "TestFixedValue",
                                        ApplyScope = applyScope
                                    };

                                    var mark = new PathMark
                                    {
                                        Path = basePath,
                                        Type = PathMarkType.Property,
                                        ConfigJson = JsonConvert.SerializeObject(config),
                                        Priority = priority++
                                    };

                                    yield return (mark, config, $"Property_Layer{layer}_{pool}_Fixed_Scope{applyScope}");
                                }
                                else // Dynamic
                                {
                                    foreach (var valueLayer in valueLayers)
                                    {
                                        foreach (var valueRegex in valueRegexPatterns)
                                        {
                                            var config = new PropertyMarkConfig
                                            {
                                                MatchMode = matchMode,
                                                Layer = layer,
                                                Pool = pool,
                                                PropertyId = pool == PropertyPool.Custom ? customPropertyId : (int)ReservedProperty.Rating,
                                                ValueType = valueType,
                                                ValueLayer = valueLayer,
                                                ValueRegex = valueRegex,
                                                ApplyScope = applyScope
                                            };

                                            var mark = new PathMark
                                            {
                                                Path = basePath,
                                                Type = PathMarkType.Property,
                                                ConfigJson = JsonConvert.SerializeObject(config),
                                                Priority = priority++
                                            };

                                            var regexDesc = valueRegex != null ? "_WithRegex" : "";
                                            yield return (mark, config, $"Property_Layer{layer}_{pool}_Dynamic_ValueLayer{valueLayer}{regexDesc}_Scope{applyScope}");
                                        }
                                    }
                                }
                            }
                        }
                        else // Regex match mode
                        {
                            foreach (var regex in regexPatterns)
                            {
                                if (valueType == PropertyValueType.Fixed)
                                {
                                    var config = new PropertyMarkConfig
                                    {
                                        MatchMode = matchMode,
                                        Regex = regex,
                                        Pool = pool,
                                        PropertyId = pool == PropertyPool.Custom ? customPropertyId : (int)ReservedProperty.Rating,
                                        ValueType = valueType,
                                        FixedValue = "TestFixedValue",
                                        ApplyScope = applyScope
                                    };

                                    var mark = new PathMark
                                    {
                                        Path = basePath,
                                        Type = PathMarkType.Property,
                                        ConfigJson = JsonConvert.SerializeObject(config),
                                        Priority = priority++
                                    };

                                    yield return (mark, config, $"Property_Regex_{pool}_Fixed_Scope{applyScope}");
                                }
                                else // Dynamic with regex match mode
                                {
                                    foreach (var valueLayer in valueLayers)
                                    {
                                        var config = new PropertyMarkConfig
                                        {
                                            MatchMode = matchMode,
                                            Regex = regex,
                                            Pool = pool,
                                            PropertyId = pool == PropertyPool.Custom ? customPropertyId : (int)ReservedProperty.Rating,
                                            ValueType = valueType,
                                            ValueLayer = valueLayer,
                                            ApplyScope = applyScope
                                        };

                                        var mark = new PathMark
                                        {
                                            Path = basePath,
                                            Type = PathMarkType.Property,
                                            ConfigJson = JsonConvert.SerializeObject(config),
                                            Priority = priority++
                                        };

                                        yield return (mark, config, $"Property_Regex_{pool}_Dynamic_ValueLayer{valueLayer}_Scope{applyScope}");
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// 生成随机 Property Mark
    /// </summary>
    public static (PathMark Mark, PropertyMarkConfig Config) GenerateRandomPropertyMark(
        string basePath,
        int customPropertyId,
        PathMatchMode? matchMode = null,
        PropertyValueType? valueType = null,
        PropertyPool? pool = null)
    {
        matchMode ??= Random.Next(2) == 0 ? PathMatchMode.Layer : PathMatchMode.Regex;
        valueType ??= Random.Next(2) == 0 ? PropertyValueType.Fixed : PropertyValueType.Dynamic;
        pool ??= Random.Next(2) == 0 ? PropertyPool.Custom : PropertyPool.Reserved;

        var config = new PropertyMarkConfig
        {
            MatchMode = matchMode.Value,
            Pool = pool.Value,
            PropertyId = pool == PropertyPool.Custom ? customPropertyId : (int)ReservedProperty.Rating,
            ValueType = valueType.Value,
            ApplyScope = Random.Next(2) == 0 ? PathMarkApplyScope.MatchedOnly : PathMarkApplyScope.MatchedAndSubdirectories
        };

        if (matchMode == PathMatchMode.Layer)
        {
            config.Layer = Random.Next(-3, 6);
        }
        else
        {
            config.Regex = @".+";
        }

        if (valueType == PropertyValueType.Fixed)
        {
            config.FixedValue = $"FixedValue_{Random.Next(1000)}";
        }
        else
        {
            config.ValueLayer = Random.Next(-3, 6);
            if (Random.Next(2) == 0)
            {
                config.ValueRegex = @"(.+)";
            }
        }

        var mark = new PathMark
        {
            Path = basePath,
            Type = PathMarkType.Property,
            ConfigJson = JsonConvert.SerializeObject(config),
            Priority = Random.Next(1, 100)
        };

        return (mark, config);
    }

    #endregion

    #region MediaLibrary Mark Generators

    /// <summary>
    /// 生成所有 MediaLibrary Mark 配置组合
    /// </summary>
    public static IEnumerable<(PathMark Mark, MediaLibraryMarkConfig Config, string Description)> GenerateAllMediaLibraryMarkCombinations(
        string basePath,
        int mediaLibraryId)
    {
        var matchModes = new[] { PathMatchMode.Layer, PathMatchMode.Regex };
        var valueTypes = new[] { PropertyValueType.Fixed, PropertyValueType.Dynamic };
        var applyScopes = new[] { PathMarkApplyScope.MatchedOnly, PathMarkApplyScope.MatchedAndSubdirectories };
        var layers = new[] { -3, -2, -1, 0, 1, 2, 3, 4, 5 };
        var layersToMediaLibrary = new[] { -3, -2, -1, 0, 1, 2, 3, 4, 5 };
        var regexPatterns = new[] { @".+", @"^Library_(.+)$" };

        var priority = 1;

        foreach (var matchMode in matchModes)
        {
            foreach (var valueType in valueTypes)
            {
                foreach (var applyScope in applyScopes)
                {
                    if (matchMode == PathMatchMode.Layer)
                    {
                        foreach (var layer in layers)
                        {
                            if (valueType == PropertyValueType.Fixed)
                            {
                                var config = new MediaLibraryMarkConfig
                                {
                                    MatchMode = matchMode,
                                    Layer = layer,
                                    ValueType = valueType,
                                    MediaLibraryId = mediaLibraryId,
                                    ApplyScope = applyScope
                                };

                                var mark = new PathMark
                                {
                                    Path = basePath,
                                    Type = PathMarkType.MediaLibrary,
                                    ConfigJson = JsonConvert.SerializeObject(config),
                                    Priority = priority++
                                };

                                yield return (mark, config, $"MediaLibrary_Layer{layer}_Fixed_Scope{applyScope}");
                            }
                            else // Dynamic
                            {
                                foreach (var layerToMl in layersToMediaLibrary)
                                {
                                    var config = new MediaLibraryMarkConfig
                                    {
                                        MatchMode = matchMode,
                                        Layer = layer,
                                        ValueType = valueType,
                                        LayerToMediaLibrary = layerToMl,
                                        ApplyScope = applyScope
                                    };

                                    var mark = new PathMark
                                    {
                                        Path = basePath,
                                        Type = PathMarkType.MediaLibrary,
                                        ConfigJson = JsonConvert.SerializeObject(config),
                                        Priority = priority++
                                    };

                                    yield return (mark, config, $"MediaLibrary_Layer{layer}_Dynamic_LayerToMl{layerToMl}_Scope{applyScope}");
                                }
                            }
                        }
                    }
                    else // Regex
                    {
                        foreach (var regex in regexPatterns)
                        {
                            if (valueType == PropertyValueType.Fixed)
                            {
                                var config = new MediaLibraryMarkConfig
                                {
                                    MatchMode = matchMode,
                                    Regex = regex,
                                    ValueType = valueType,
                                    MediaLibraryId = mediaLibraryId,
                                    ApplyScope = applyScope
                                };

                                var mark = new PathMark
                                {
                                    Path = basePath,
                                    Type = PathMarkType.MediaLibrary,
                                    ConfigJson = JsonConvert.SerializeObject(config),
                                    Priority = priority++
                                };

                                yield return (mark, config, $"MediaLibrary_Regex_Fixed_Scope{applyScope}");
                            }
                            else // Dynamic
                            {
                                foreach (var layerToMl in layersToMediaLibrary)
                                {
                                    var config = new MediaLibraryMarkConfig
                                    {
                                        MatchMode = matchMode,
                                        Regex = regex,
                                        ValueType = valueType,
                                        LayerToMediaLibrary = layerToMl,
                                        ApplyScope = applyScope
                                    };

                                    var mark = new PathMark
                                    {
                                        Path = basePath,
                                        Type = PathMarkType.MediaLibrary,
                                        ConfigJson = JsonConvert.SerializeObject(config),
                                        Priority = priority++
                                    };

                                    yield return (mark, config, $"MediaLibrary_Regex_Dynamic_LayerToMl{layerToMl}_Scope{applyScope}");
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// 生成随机 MediaLibrary Mark
    /// </summary>
    public static (PathMark Mark, MediaLibraryMarkConfig Config) GenerateRandomMediaLibraryMark(
        string basePath,
        int mediaLibraryId,
        PathMatchMode? matchMode = null,
        PropertyValueType? valueType = null)
    {
        matchMode ??= Random.Next(2) == 0 ? PathMatchMode.Layer : PathMatchMode.Regex;
        valueType ??= Random.Next(2) == 0 ? PropertyValueType.Fixed : PropertyValueType.Dynamic;

        var config = new MediaLibraryMarkConfig
        {
            MatchMode = matchMode.Value,
            ValueType = valueType.Value,
            ApplyScope = Random.Next(2) == 0 ? PathMarkApplyScope.MatchedOnly : PathMarkApplyScope.MatchedAndSubdirectories
        };

        if (matchMode == PathMatchMode.Layer)
        {
            config.Layer = Random.Next(-3, 6);
        }
        else
        {
            config.Regex = @".+";
        }

        if (valueType == PropertyValueType.Fixed)
        {
            config.MediaLibraryId = mediaLibraryId;
        }
        else
        {
            config.LayerToMediaLibrary = Random.Next(-3, 6);
            if (Random.Next(2) == 0)
            {
                config.RegexToMediaLibrary = @"(.+)";
            }
        }

        var mark = new PathMark
        {
            Path = basePath,
            Type = PathMarkType.MediaLibrary,
            ConfigJson = JsonConvert.SerializeObject(config),
            Priority = Random.Next(1, 100)
        };

        return (mark, config);
    }

    #endregion

    #region Directory Structure Generator

    /// <summary>
    /// 创建测试目录结构
    /// </summary>
    public static TestDirectoryStructure CreateTestDirectoryStructure(string basePath)
    {
        var structure = new TestDirectoryStructure { BasePath = basePath };

        // 创建多层目录结构
        var level1Dirs = new[] { "Series_A", "Series_B", "Library_Main" };
        var level2Dirs = new[] { "Episode01", "Episode02", "Special" };
        var files = new[] { "video.mp4", "subtitle.srt", "cover.jpg" };

        foreach (var l1 in level1Dirs)
        {
            var l1Path = Path.Combine(basePath, l1);
            Directory.CreateDirectory(l1Path);
            structure.Level1Directories.Add(l1Path);

            foreach (var l2 in level2Dirs)
            {
                var l2Path = Path.Combine(l1Path, l2);
                Directory.CreateDirectory(l2Path);
                structure.Level2Directories.Add(l2Path);

                foreach (var f in files)
                {
                    var fPath = Path.Combine(l2Path, f);
                    File.WriteAllText(fPath, "test content");
                    structure.Files.Add(fPath);
                }
            }
        }

        return structure;
    }

    #endregion
}

/// <summary>
/// 测试目录结构信息
/// </summary>
public class TestDirectoryStructure
{
    public string BasePath { get; set; } = null!;
    public List<string> Level1Directories { get; } = new();
    public List<string> Level2Directories { get; } = new();
    public List<string> Files { get; } = new();

    public void Cleanup()
    {
        if (Directory.Exists(BasePath))
        {
            try { Directory.Delete(BasePath, true); } catch { }
        }
    }
}
