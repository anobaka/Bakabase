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
    string Unknown();
    string Decompress();
    string MoveFiles();
    string MoveFile(string src, string dest);
    string MoveResourceDetail(string srcPath, string mediaLibraryName, string destPath);
    string MoveResource();
    string BTask_Name(string key);
    string? BTask_Description(string key);
    string? BTask_MessageOnInterruption(string key);
    string? MessageOnInterruption_MoveFiles();
    string BTask_FailedToRunTaskDueToConflict(string incomingTaskName, params string[] conflictTaskNames);
    string BTask_FailedToRunTaskDueToUnknownTaskId(string id);
    string BTask_FailedToRunTaskDueToIdExisting(string id);
    string? WrongPassword();
    string DeletingInvalidResources(int count);
    string DownloadTaskParser_ParseAll_TaskName();
}