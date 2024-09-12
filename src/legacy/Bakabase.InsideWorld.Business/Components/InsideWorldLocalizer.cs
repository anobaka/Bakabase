using System;
using System.Collections.Generic;
using System.Linq;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.CustomProperty.Abstractions.Components;
using Bakabase.Modules.Enhancer.Abstractions;
using Bakabase.Modules.Enhancer.Models.Domain.Constants;
using Bakabase.Modules.StandardValue.Abstractions.Components;
using Bootstrap.Extensions;
using Microsoft.Extensions.Localization;

namespace Bakabase.InsideWorld.Business.Components
{
    /// <summary>
    /// todo: Redirect raw <see cref="IStringLocalizer"/> callings to here
    /// </summary>
    public class InsideWorldLocalizer(IStringLocalizer<Business.SharedResource> localizer)
        : IStringLocalizer<Business.SharedResource>, IBakabaseLocalizer, ICustomPropertyLocalizer,
            IStandardValueLocalizer
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

        public string Reserved_Resource_Property_Name(ReservedResourceProperty property)
        {
            return property switch
            {
                ReservedResourceProperty.Introduction or ReservedResourceProperty.Rating => this[
                    $"{nameof(Reserved_Resource_Property_Name)}_{property}"],
                _ => throw new ArgumentOutOfRangeException(nameof(property), property, null)
            };
        }

        public string Property_DescriptorIsNotFound(ResourcePropertyType type, int propertyId)
        {
            return this[nameof(Property_DescriptorIsNotFound), type, propertyId];
        }

        public string BuiltinPropertyNameForDisplayName(BuiltinPropertyForDisplayName property)
        {
            return this[$"{nameof(BuiltinPropertyForDisplayName)}.{property.ToString()}"];
        }

        public string CustomProperty_DescriptorNotFound(int propertyType)
        {
            return this[nameof(CustomProperty_DescriptorNotFound), propertyType];
        }

        public string StandardValue_HandlerNotFound(StandardValueType type)
        {
            return this[nameof(StandardValue_HandlerNotFound), type];
        }
    }
}