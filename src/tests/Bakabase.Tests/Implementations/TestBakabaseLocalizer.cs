using System.Collections.Generic;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants;
using Microsoft.Extensions.Localization;

namespace Bakabase.Tests.Implementations;

public class TestBakabaseLocalizer : IBakabaseLocalizer
{
    public LocalizedString this[string name] => new(name, name);
    public LocalizedString this[string name, params object?[] arguments] => new(name, string.Format(name, arguments));

    public string Component_NotDeletableWhenUsingByCategories(IEnumerable<string> categoryNames) => "Component_NotDeletableWhenUsingByCategories";
    public string MediaLibraryTemplate_NotDeletableWhenUsingByMediaLibraries(IEnumerable<string> mediaLibraryNames) => "MediaLibraryTemplate_NotDeletableWhenUsingByMediaLibraries";
    public string Category_Invalid((string Name, string Error)[] nameAndErrors) => "Category_Invalid";
    public string CookieValidation_Fail(string url, string? message, string? content) => "CookieValidation_Fail";
    public string Resource_NotFound(int id) => $"Resource_NotFound_{id}";
    public string Resource_CoverMustBeInDirectory() => "Resource_CoverMustBeInDirectory";
    public string Resource_MovingTaskSummary(string[]? resourceNames, string? mediaLibraryName, string destPath) => "Resource_MovingTaskSummary";
    public string PathsShouldBeInSameDirectory() => "PathsShouldBeInSameDirectory";
    public string PathIsNotFound(string path) => $"PathIsNotFound_{path}";
    public string FileNotFoundInPath(string path, params string[] files) => "FileNotFoundInPath";
    public string ValueIsNotSet(string name) => $"ValueIsNotSet_{name}";
    public string NewFolderName() => "NewFolderName";
    public string Downloader_FailedToStart(string taskName, string message) => "Downloader_FailedToStart";
    public string SpecialText_HistoricalLanguageValue2ShouldBeModified() => "SpecialText_HistoricalLanguageValue2ShouldBeModified";
    public string Reserved_Resource_Property_Name(ReservedProperty property) => property.ToString();
    public string Unknown() => "Unknown";
    public string Failed() => "Failed";
    public string Decompress() => "Decompress";
    public string MoveFiles() => "MoveFiles";
    public string MoveFile(string src, string dest) => $"MoveFile_{src}_{dest}";
    public string MoveResourceDetail(string srcPath, string mediaLibraryName, string destPath) => "MoveResourceDetail";
    public string MoveResource() => "MoveResource";
    public string CopyFiles() => "CopyFiles";
    public string CopyFile(string src, string dest) => $"CopyFile_{src}_{dest}";
    public string? MessageOnInterruption_CopyFiles() => "MessageOnInterruption_CopyFiles";
    public string BTask_Name(string key) => $"BTask_{key}";
    public string? BTask_Description(string key) => $"BTask_Description_{key}";
    public string? BTask_MessageOnInterruption(string key) => $"BTask_MessageOnInterruption_{key}";
    public string? MessageOnInterruption_MoveFiles() => "MessageOnInterruption_MoveFiles";
    public string BTask_FailedToRunTaskDueToConflict(string incomingTaskName, params string[] conflictTaskNames) => "BTask_FailedToRunTaskDueToConflict";
    public string BTask_FailedToRunTaskDueToDependency(string incomingTaskName, params string[] dependencyTaskNames) => $"Task {incomingTaskName} waiting for: {string.Join(", ", dependencyTaskNames)}";
    public string BTask_FailedToRunTaskDueToUnknownTaskId(string id) => $"BTask_FailedToRunTaskDueToUnknownTaskId_{id}";
    public string BTask_FailedToRunTaskDueToIdExisting(string id, string name) => "BTask_FailedToRunTaskDueToIdExisting";
    public string BTask_CanNotReplaceAnActiveTask(string id, string name) => "BTask_CanNotReplaceAnActiveTask";
    public string? WrongPassword() => "WrongPassword";
    public string DeletingInvalidResources(int count) => $"DeletingInvalidResources_{count}";
    public string PostParser_ParseAll_TaskName() => "PostParser_ParseAll_TaskName";
    public string SyncMediaLibrary(string name) => $"SyncMediaLibrary_{name}";
    public string SyncMediaLibrary_TaskProcess_DiscoverResources(string name) => "SyncMediaLibrary_TaskProcess_DiscoverResources";
    public string SyncMediaLibrary_TaskProcess_CleanupResources(string name) => "SyncMediaLibrary_TaskProcess_CleanupResources";
    public string SyncMediaLibrary_TaskProcess_AddResources(string name) => "SyncMediaLibrary_TaskProcess_AddResources";
    public string SyncMediaLibrary_TaskProcess_UpdateResources(string name) => "SyncMediaLibrary_TaskProcess_UpdateResources";
    public string SyncMediaLibrary_TaskProcess_AlmostComplete(string name) => "SyncMediaLibrary_TaskProcess_AlmostComplete";
    public string MediaType(MediaType type) => type.ToString();
    public string Resource() => "Resource";
    public string Search() => "Search";
    public string Searching() => "Searching";
    public string Found() => "Found";
    public string NotSet() => "NotSet";
    public string Count() => "Count";
    public string Keyword() => "Keyword";
    public string Enhancer_CircularDependencyOrUnsatisfiedPredecessorsDetected(string[] enhancers) => "Enhancer_CircularDependencyOrUnsatisfiedPredecessorsDetected";
    public string MediaLibraryTemplate_ValidationTraceTopic(string topic) => $"MediaLibraryTemplate_ValidationTraceTopic_{topic}";
    public string MediaLibraryTemplate_Name() => "MediaLibraryTemplate_Name";
    public string MediaLibraryTemplate_Id() => "MediaLibraryTemplate_Id";

    // Validation trace topics and labels
    public string Init() => "Init";
    public string ResourceDiscovery() => "ResourceDiscovery";
    public string PickResourcesToValidate() => "PickResourcesToValidate";
    public string PropertyValuesGeneratedOnSynchronization() => "PropertyValuesGeneratedOnSynchronization";
    public string NoPropertyValuesGeneratedOnSynchronization() => "NoPropertyValuesGeneratedOnSynchronization";
    public string PropertyValuesGeneratedByEnhancer() => "PropertyValuesGeneratedByEnhancer";
    public string NoPropertyValuesGeneratedByEnhancer() => "NoPropertyValuesGeneratedByEnhancer";
    public string DiscoveringPlayableFiles() => "DiscoveringPlayableFiles";
    public string FoundPlayableFiles() => "FoundPlayableFiles";
    public string NoPlayableFiles() => "NoPlayableFiles";
    public string NoExtensionsConfigured() => "NoExtensionsConfigured";
    public string NoPlayableFileLocatorConfigured() => "NoPlayableFileLocatorConfigured";
    public string RunningEnhancers() => "RunningEnhancers";
    public string StartEnhancing() => "StartEnhancing";
    public string EnhancementCompleted() => "EnhancementCompleted";
    public string ResourceEnhanced() => "ResourceEnhanced";
    public string Enhancer() => "Enhancer";
    public string NoEnhancerConfigured() => "NoEnhancerConfigured";
    public string Context() => "Context";
    public string ResourceDisplayName() => "ResourceDisplayName";
    public string DisplayName() => "DisplayName";
    public string Summary() => "Summary";
    public string Complete() => "Complete";
    public string PlayableFiles() => "PlayableFiles";
    public string BuildingData() => "BuildingData";

    // PathMark Sync
    public string SyncPathMark_Collecting() => "SyncPathMark_Collecting";
    public string SyncPathMark_Collected(int count) => $"SyncPathMark_Collected_{count}";
    public string SyncPathMark_ProcessingResource(string path) => $"SyncPathMark_ProcessingResource_{path}";
    public string SyncPathMark_ProcessingProperty(string path) => $"SyncPathMark_ProcessingProperty_{path}";
    public string SyncPathMark_ProcessingMediaLibrary(string path) => $"SyncPathMark_ProcessingMediaLibrary_{path}";
    public string SyncPathMark_FindingRelated() => "SyncPathMark_FindingRelated";
    public string SyncPathMark_FoundRelated(int count) => $"SyncPathMark_FoundRelated_{count}";
    public string SyncPathMark_EstablishingRelationships() => "SyncPathMark_EstablishingRelationships";
    public string SyncPathMark_Complete() => "SyncPathMark_Complete";
}
