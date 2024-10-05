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
}