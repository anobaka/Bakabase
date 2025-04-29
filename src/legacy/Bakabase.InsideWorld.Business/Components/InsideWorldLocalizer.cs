using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.ComTypes;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Business.Components.Dependency.Abstractions;
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
    public class InsideWorldLocalizer(IStringLocalizer<Business.SharedResource> localizer)
        : IStringLocalizer<Business.SharedResource>, IBakabaseLocalizer, IDependencyLocalizer
    {
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) =>
            localizer.GetAllStrings(includeParentCultures);

        public LocalizedString this[string name] => localizer[name];

        public LocalizedString this[string name, params object?[] arguments] => localizer[name, arguments];

        public string Component_NotDeletableWhenUsingByCategories(IEnumerable<string> categoryNames) =>
            this[nameof(Component_NotDeletableWhenUsingByCategories), string.Join(',', categoryNames)];

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
            return r.ResourceNotFound ? null : (string?) r;
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

        public string BTask_FailedToRunTaskDueToIdExisting(string id) =>
            this[nameof(BTask_FailedToRunTaskDueToIdExisting), id];

        public string Property_DescriptorIsNotFound(PropertyPool type, int propertyId)
        {
            return this[nameof(Property_DescriptorIsNotFound), type, propertyId];
        }

        public string? Dependency_Component_Name(string key)
        {
            var d = this[$"Dependency_Component_{key}_Name"];
            return d.ResourceNotFound ? null : (string?) d;
        }

        public string? Dependency_Component_Description(string key)
        {
            var d = this[$"Dependency_Component_{key}_Description"];
            return d.ResourceNotFound ? null : (string?) d;
        }

        public string? Name(BackgroundTaskName name)
        {
            var d = this[$"BackgroundTask_{name}_Name"];
            return d.ResourceNotFound ? null : (string?) d;
        }

        public string? WrongPassword()
        {
            return this[nameof(WrongPassword)];
        }

        public string DeletingInvalidResources(int count)
        {
            return this[nameof(DeletingInvalidResources), count];
        }
    }
}