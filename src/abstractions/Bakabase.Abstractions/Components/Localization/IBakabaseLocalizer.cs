using JetBrains.Annotations;
using Microsoft.Extensions.Localization;
using System.Diagnostics.CodeAnalysis;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.Abstractions.Components.Localization;

public interface IBakabaseLocalizer
{
    LocalizedString this[string name] { get; }
    LocalizedString this[string name, params object?[] arguments] { get; }
    string Component_NotDeletableWhenUsingByCategories(IEnumerable<string> categoryNames);
    string MediaLibraryTemplate_NotDeletableWhenUsingByMediaLibraries(IEnumerable<string> mediaLibraryNames);
    string Category_Invalid((string Name, string Error)[] nameAndErrors);
    string CookieValidation_Fail(string url, string? message, string? content);
    string Resource_NotFound(int id);
    string Resource_CoverMustBeInDirectory();
    string Resource_MovingTaskSummary(string[]? resourceNames, string? mediaLibraryName, string destPath);
    string PathsShouldBeInSameDirectory();
    string PathIsNotFound(string path);
    string FileNotFoundInPath(string path, params string[] files);
    string ValueIsNotSet(string name);
    string NewFolderName();
    string Downloader_FailedToStart(string taskName, string message);
    string SpecialText_HistoricalLanguageValue2ShouldBeModified();
    string Reserved_Resource_Property_Name(ReservedProperty property);
    string Unknown();
    string Failed();
    string Decompress();
    string MoveFiles();
    string MoveFile(string src, string dest);
    string MoveResourceDetail(string srcPath, string mediaLibraryName, string destPath);
    string MoveResource();
    string CopyFiles();
    string CopyFile(string src, string dest);
    string? MessageOnInterruption_CopyFiles();
    string BTask_Name(string key);
    string? BTask_Description(string key);
    string? BTask_MessageOnInterruption(string key);
    string? MessageOnInterruption_MoveFiles();
    string BTask_FailedToRunTaskDueToConflict(string incomingTaskName, params string[] conflictTaskNames);
    string BTask_FailedToRunTaskDueToDependency(string incomingTaskName, params string[] dependencyTaskNames);
    string BTask_FailedToRunTaskDueToUnknownTaskId(string id);
    string BTask_FailedToRunTaskDueToIdExisting(string id, string name);
    string BTask_CanNotReplaceAnActiveTask(string id, string name);
    string? WrongPassword();
    string DeletingInvalidResources(int count);
    string PostParser_ParseAll_TaskName();
    string SyncMediaLibrary(string name);
    string SyncMediaLibrary_TaskProcess_DiscoverResources(string name);
    string SyncMediaLibrary_TaskProcess_CleanupResources(string name);
    string SyncMediaLibrary_TaskProcess_AddResources(string name);
    string SyncMediaLibrary_TaskProcess_UpdateResources(string name);
    string SyncMediaLibrary_TaskProcess_AlmostComplete(string name);
    string MediaType(MediaType type);
    string Resource();
    string Search();
    string Searching();
    string Found();
    string NotSet();
    string Count();
    string Keyword();
    string Enhancer_CircularDependencyOrUnsatisfiedPredecessorsDetected(string[] enhancers);
    string MediaLibraryTemplate_ValidationTraceTopic(string topic);
    string MediaLibraryTemplate_Name();
    string MediaLibraryTemplate_Id();

    // Validation trace topics and labels
    string Init();
    string ResourceDiscovery();
    string PickResourcesToValidate();
    string PropertyValuesGeneratedOnSynchronization();
    string NoPropertyValuesGeneratedOnSynchronization();
    string PropertyValuesGeneratedByEnhancer();
    string NoPropertyValuesGeneratedByEnhancer();
    string DiscoveringPlayableFiles();
    string FoundPlayableFiles();
    string NoPlayableFiles();
    string NoExtensionsConfigured();
    string NoPlayableFileLocatorConfigured();
    string RunningEnhancers();
    string StartEnhancing();
    string EnhancementCompleted();
    string ResourceEnhanced();
    string Enhancer();
    string NoEnhancerConfigured();
    string Context();
    string ResourceDisplayName();
    string DisplayName();
    string Summary();
    string Complete();
    string PlayableFiles();
    string BuildingData();

    // PathMark Sync
    string SyncPathMark_Collecting();
    string SyncPathMark_Collected(int count);
    string SyncPathMark_ProcessingResource(string path);
    string SyncPathMark_ProcessingProperty(string path);
    string SyncPathMark_ProcessingMediaLibrary(string path);
    string SyncPathMark_FindingRelated();
    string SyncPathMark_FoundRelated(int count);
    string SyncPathMark_EstablishingRelationships();
    string SyncPathMark_Complete();
}