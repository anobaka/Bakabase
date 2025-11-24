using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.ComTypes;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Business.Components.Dependency.Abstractions;
using Bakabase.InsideWorld.Business.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.Enhancer.Abstractions;
using Bakabase.Modules.Enhancer.Models.Domain.Constants;
using Bakabase.Modules.Property.Abstractions.Components;
using Bakabase.Modules.StandardValue.Abstractions.Components;
using Bootstrap.Extensions;
using Microsoft.Extensions.Localization;

namespace Bakabase.InsideWorld.Business.Components
{
    /// <summary>
    /// todo: Redirect raw <see cref="IStringLocalizer"/> callings to here
    /// </summary>
    public class BakabaseLocalizer(IStringLocalizer<Business.SharedResource> localizer)
        : IStringLocalizer<Business.SharedResource>, IBakabaseLocalizer, IDependencyLocalizer
    {
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) =>
            localizer.GetAllStrings(includeParentCultures);

        public LocalizedString this[string name] => localizer[name];

        public LocalizedString this[string name, params object?[] arguments] => localizer[name, arguments];

        public string Component_NotDeletableWhenUsingByCategories(IEnumerable<string> categoryNames) =>
            this[nameof(Component_NotDeletableWhenUsingByCategories), string.Join(',', categoryNames)];

        public string MediaLibraryTemplate_NotDeletableWhenUsingByMediaLibraries(IEnumerable<string> mediaLibraryNames) =>
            this[nameof(MediaLibraryTemplate_NotDeletableWhenUsingByMediaLibraries), string.Join(',', mediaLibraryNames)];

        public string Category_Invalid((string Name, string Error)[] nameAndErrors) => this[nameof(Category_Invalid),
            string.Join(Environment.NewLine, nameAndErrors.Select(a => $"{a.Name}:{a.Error}"))];

        public string CookieValidation_Fail(string url, string? message, string? content)
        {
            const int maxContentLength = 1000;
            var shortContent = content.Length > maxContentLength ? content[..maxContentLength] : content;
            return this[nameof(CookieValidation_Fail), url, message, shortContent];
        }

        public string Resource_NotFound(int id) => this[nameof(Resource_NotFound), id];
        public string Resource_CoverMustBeInDirectory() => this[nameof(Resource_CoverMustBeInDirectory)];

        public string Resource_MovingTaskSummary(string[]? resourceNames, string? mediaLibraryName, string destPath)
        {
            if (resourceNames != null)
            {
                return mediaLibraryName.IsNotEmpty()
                    ? Resource_MovingTaskSummaryWithMediaLibrary(resourceNames, mediaLibraryName!, destPath)
                    : this[nameof(Resource_MovingTaskSummary), string.Join(',', resourceNames), destPath];
            }

            return string.IsNullOrEmpty(mediaLibraryName)
                ? Resource_MovingTaskSummaryForSingleResource(destPath)
                : Resource_MovingTaskSummaryForSingleResourceWithMediaLibrary(mediaLibraryName, destPath);
        }

        private string Resource_MovingTaskSummaryWithMediaLibrary(string[] resourceNames, string mediaLibraryName,
            string destPath) => this[nameof(Resource_MovingTaskSummaryWithMediaLibrary),
            string.Join(',', resourceNames), mediaLibraryName, destPath];

        private string Resource_MovingTaskSummaryForSingleResource(string destPath) =>
            this[nameof(Resource_MovingTaskSummaryForSingleResource), destPath];

        private string
            Resource_MovingTaskSummaryForSingleResourceWithMediaLibrary(string mediaLibraryName, string destPath) =>
            this[nameof(Resource_MovingTaskSummaryForSingleResourceWithMediaLibrary), mediaLibraryName, destPath];

        public string PathsShouldBeInSameDirectory() => this[nameof(PathsShouldBeInSameDirectory)];

        public string PathIsNotFound(string path) => this[nameof(PathIsNotFound), path];

        public string FileNotFoundInPath(string path, params string[] files) => files.Length > 1
            ? FilesDoNotExistInPath(files, path)
            : FileDoesNotExistInPath(files.Any() ? files[0] : "unknown", path);

        private string FilesDoNotExistInPath(string[] files, string path) =>
            this[nameof(FilesDoNotExistInPath), string.Join(',', files), path];

        private string FileDoesNotExistInPath(string file, string path) =>
            this[nameof(FileDoesNotExistInPath), file, path];

        private string Resource_CannotSaveCoverToCurrentDirectoryForSingleFileResource() =>
            this[nameof(Resource_CannotSaveCoverToCurrentDirectoryForSingleFileResource)];

        public string ValueIsNotSet(string name) => this[nameof(ValueIsNotSet), name];

        public string NewFolderName() => this[nameof(NewFolderName)];

        public string Downloader_FailedToStart(string taskName, string message) =>
            this[nameof(Downloader_FailedToStart), taskName, message];

        public string SpecialText_HistoricalLanguageValue2ShouldBeModified() =>
            this[nameof(SpecialText_HistoricalLanguageValue2ShouldBeModified)];

        public string Reserved_Resource_Property_Name(Abstractions.Models.Domain.Constants.ReservedProperty property) =>
            this[$"{nameof(Reserved_Resource_Property_Name)}_{property}"];

        public string Unknown() => this[nameof(Unknown)];
        public string Failed()
        {
            return this[nameof(Failed)];
        }

        public string Decompress() => this[nameof(Decompress)];
        public string MoveFiles() => this[nameof(MoveFiles)];
        public string MoveFile(string src, string dest) => this[nameof(MoveFile), src, dest];

        public string MoveResourceDetail(string srcPath, string mediaLibraryName, string destPath) =>
            this[nameof(MoveResourceDetail), srcPath, mediaLibraryName, destPath];

        public string MoveResource() => this[nameof(MoveResource)];

        public string BTask_Name(string key) => this[$"{nameof(BTask_Name)}_{key}"];

        public string? BTask_Description(string key)
        {
            var r = this[$"{nameof(BTask_Description)}_{key}"];
            return r.ResourceNotFound ? null : (string?)r;
        }

        public string? BTask_MessageOnInterruption(string key)
        {
            var r = this[$"{nameof(BTask_MessageOnInterruption)}_{key}"];
            return r.ResourceNotFound ? null : (string?)r;
        }

        public string? MessageOnInterruption_MoveFiles() => BTask_MessageOnInterruption("MoveFiles");

        public string BTask_FailedToRunTaskDueToConflict(string incomingTaskName, params string[] conflictTaskNames) =>
            this[nameof(BTask_FailedToRunTaskDueToConflict), incomingTaskName, string.Join(',', conflictTaskNames)];

        public string BTask_FailedToRunTaskDueToUnknownTaskId(string id) =>
            this[nameof(BTask_FailedToRunTaskDueToUnknownTaskId), id];

        public string BTask_FailedToRunTaskDueToIdExisting(string id, string name) =>
            this[nameof(BTask_FailedToRunTaskDueToIdExisting), id, name];

        public string BTask_CanNotReplaceAnActiveTask(string id, string name) =>
            this[nameof(BTask_CanNotReplaceAnActiveTask), id, name];

        public string Property_DescriptorIsNotFound(PropertyPool type, int propertyId)
        {
            return this[nameof(Property_DescriptorIsNotFound), type, propertyId];
        }

        public string? Dependency_Component_Name(string key)
        {
            var d = this[$"Dependency_Component_{key}_Name"];
            return d.ResourceNotFound ? null : (string?)d;
        }

        public string? Dependency_Component_Description(string key)
        {
            var d = this[$"Dependency_Component_{key}_Description"];
            return d.ResourceNotFound ? null : (string?)d;
        }

        public string? Name(BackgroundTaskName name)
        {
            var d = this[$"BackgroundTask_{name}_Name"];
            return d.ResourceNotFound ? null : (string?)d;
        }

        public string? WrongPassword()
        {
            return this[nameof(WrongPassword)];
        }

        public string DeletingInvalidResources(int count)
        {
            return this[nameof(DeletingInvalidResources), count];
        }

        public string PostParser_ParseAll_TaskName()
        {
            return this[nameof(PostParser_ParseAll_TaskName)];
        }

        public string SyncMediaLibrary(string name)
        {
            return this[nameof(SyncMediaLibrary), name];
        }

        public string SyncMediaLibrary_TaskProcess_DiscoverResources(string name)
        {
            return this[nameof(SyncMediaLibrary_TaskProcess_DiscoverResources), name];
        }

        public string SyncMediaLibrary_TaskProcess_CleanupResources(string name)
        {
            return this[nameof(SyncMediaLibrary_TaskProcess_CleanupResources), name];
        }

        public string SyncMediaLibrary_TaskProcess_AddResources(string name)
        {
            return this[nameof(SyncMediaLibrary_TaskProcess_AddResources), name];
        }

        public string SyncMediaLibrary_TaskProcess_UpdateResources(string name)
        {
            return this[nameof(SyncMediaLibrary_TaskProcess_UpdateResources), name];
        }

        public string SyncMediaLibrary_TaskProcess_AlmostComplete(string name)
        {
            return this[nameof(SyncMediaLibrary_TaskProcess_AlmostComplete), name];
        }

        public string MediaType(MediaType type)
        {
            return this[$"{nameof(MediaType)}_{type}"];
        }

        public string Resource()
        {
            return this[nameof(Resource)];
        }

        public string Search()
        {
            return this[nameof(Search)];
        }

        public string Searching()
        {
            return this[nameof(Searching)];
        }

        public string Found()
        {
            return this[nameof(Found)];
        }

        public string NotSet()
        {
            return this[nameof(NotSet)];
        }

        public string Count()
        {
            return this[nameof(Count)];
        }

        public string Keyword()
        {
            return this[nameof(Keyword)];
        }

        public string Enhancer_CircularDependencyOrUnsatisfiedPredecessorsDetected(string[] enhancers)
        {
            return this[nameof(Enhancer_CircularDependencyOrUnsatisfiedPredecessorsDetected), string.Join("->", enhancers)];
        }

        public string MediaLibraryTemplate_ValidationTraceTopic(string topic) => topic;

        public string MediaLibraryTemplate_Name()
        {
            return this[nameof(MediaLibraryTemplate_Name)];
        }

        public string MediaLibraryTemplate_Id()
        {
            return this[nameof(MediaLibraryTemplate_Id)];
        }

        public string Init()
        {
            return this[nameof(Init)];
        }

        public string ResourceDiscovery()
        {
            return this[nameof(ResourceDiscovery)];
        }

        public string PickResourcesToValidate()
        {
            return this[nameof(PickResourcesToValidate)];
        }
        
        public string PropertyValuesGeneratedOnSynchronization()
        {
            return this[nameof(PropertyValuesGeneratedOnSynchronization)];
        }

        public string NoPropertyValuesGeneratedOnSynchronization()
        {
            return this[nameof(NoPropertyValuesGeneratedOnSynchronization)];
        }

        public string PropertyValuesGeneratedByEnhancer()
        {
            return this[nameof(PropertyValuesGeneratedByEnhancer)];
        }

        public string NoPropertyValuesGeneratedByEnhancer()
        {
            return this[nameof(NoPropertyValuesGeneratedByEnhancer)];
        }

        public string DiscoveringPlayableFiles()
        {
            return this[nameof(DiscoveringPlayableFiles)];
        }

        public string FoundPlayableFiles()
        {
            return this[nameof(FoundPlayableFiles)];
        }

        public string NoPlayableFiles()
        {
            return this[nameof(NoPlayableFiles)];
        }

        public string NoExtensionsConfigured()
        {
            return this[nameof(NoExtensionsConfigured)];
        }

        public string NoPlayableFileLocatorConfigured()
        {
            return this[nameof(NoPlayableFileLocatorConfigured)];
        }

        public string RunningEnhancers()
        {
            return this[nameof(RunningEnhancers)];
        }

        public string StartEnhancing()
        {
            return this[nameof(StartEnhancing)];
        }

        public string EnhancementCompleted()
        {
            return this[nameof(EnhancementCompleted)];
        }

        public string ResourceEnhanced()
        {
            return this[nameof(ResourceEnhanced)];
        }

        public string Enhancer()
        {
            return this[nameof(Enhancer)];
        }

        public string NoEnhancerConfigured()
        {
            return this[nameof(NoEnhancerConfigured)];
        }

        public string Context()
        {
            return this[nameof(Context)];
        }

        public string ResourceDisplayName()
        {
            return this[nameof(ResourceDisplayName)];
        }

        public string DisplayName()
        {
            return this[nameof(DisplayName)];
        }

        public string Summary()
        {
            return this[nameof(Summary)];
        }

        public string Complete()
        {
            return this[nameof(Complete)];
        }

        public string PlayableFiles()
        {
            return this[nameof(PlayableFiles)];
        }

        public string BuildingData()
        {
            return this[nameof(BuildingData)];
        }

        public string VersionCheck_NewVersionAvailableTitle()
        {
            return this[nameof(VersionCheck_NewVersionAvailableTitle)];
        }

        public string VersionCheck_NewVersionAvailableMessage(string version)
        {
            return this[nameof(VersionCheck_NewVersionAvailableMessage), version];
        }
    }
}