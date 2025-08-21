using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.AccessControl;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.Infrastructures.Components.Gui;
using Bakabase.InsideWorld.Business.Components;
using Bakabase.InsideWorld.Business.Components.Compression;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.FfMpeg;
using Bakabase.InsideWorld.Business.Components.FileExplorer;
using Bakabase.InsideWorld.Business.Components.FileExplorer.Entries;
using Bakabase.InsideWorld.Business.Components.FileExplorer.Information;
using Bakabase.InsideWorld.Business.Extensions;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.InsideWorld.Models.Models.Aos;
using Bakabase.InsideWorld.Models.RequestModels;
using Bakabase.Service.Models.Input;
using Bakabase.Service.Models.View;
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Components.Cryptography;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Components.Storage;
using Bootstrap.Components.Tasks;
using Bootstrap.Extensions;
using Bootstrap.Models.Constants;
using Bootstrap.Models.ResponseModels;
using CliWrap;
using CliWrap.Buffered;
using CsQuery.ExtensionMethods.Internal;
using DotNext.Collections.Generic;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Org.BouncyCastle.Math;
using SharpCompress.Common;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers
{
    [Route("~/file")]
    public class FileController : Controller
    {
        private readonly ISpecialTextService _specialTextService;
        private readonly IWebHostEnvironment _env;
        private readonly BTaskManager _taskManager;
        private readonly string _sevenZExecutable;
        private readonly CompressedFileService _compressedFileService;
        private readonly IBOptionsManager<FileSystemOptions> _fsOptionsManager;
        private readonly BakabaseLocalizer _localizer;
        private readonly IwFsWatcher _fileProcessorWatcher;
        private readonly PasswordService _passwordService;
        private readonly ILogger<FileController> _logger;
        private readonly IGuiAdapter _guiAdapter;
        private readonly FfMpegService _ffMpegService;
        private readonly HardwareAccelerationService _hardwareAccelerationService;

        public FileController(ISpecialTextService specialTextService, IWebHostEnvironment env,
            CompressedFileService compressedFileService, IBOptionsManager<FileSystemOptions> fsOptionsManager,
            IwFsWatcher fileProcessorWatcher, PasswordService passwordService, ILogger<FileController> logger,
            BakabaseLocalizer localizer, BTaskManager taskManager, IGuiAdapter guiAdapter, 
            FfMpegService ffMpegService, HardwareAccelerationService hardwareAccelerationService)
        {
            _specialTextService = specialTextService;
            _env = env;
            _compressedFileService = compressedFileService;
            _fsOptionsManager = fsOptionsManager;
            _fileProcessorWatcher = fileProcessorWatcher;
            _passwordService = passwordService;
            _logger = logger;
            _localizer = localizer;
            _taskManager = taskManager;
            _guiAdapter = guiAdapter;
            _ffMpegService = ffMpegService;
            _hardwareAccelerationService = hardwareAccelerationService;

            _sevenZExecutable = Path.Combine(_env.ContentRootPath, "libs/7z.exe");
        }

        [HttpGet("top-level-file-system-entries")]
        [SwaggerOperation(OperationId = "GetTopLevelFileSystemEntryNames")]
        public async Task<ListResponse<FileSystemEntryNameViewModel>> GetTopLevelFileSystemEntryNames(
            string root)
        {
            var files = Directory.GetFiles(root)
                .Select(x => new FileSystemEntryNameViewModel(x.StandardizePath()!, Path.GetFileName(x), false)).ToArray();
            var directories = Directory.GetDirectories(root)
                .Select(x => new FileSystemEntryNameViewModel(x.StandardizePath()!, Path.GetFileName(x)!, true)).ToArray();

            return new ListResponse<FileSystemEntryNameViewModel>(files.Concat(directories)
                .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase));
        }

        [HttpGet("search-fs-entries")]
        [SwaggerOperation(OperationId = "SearchFileSystemEntries")]
        public async Task<ListResponse<FileSystemEntryNameViewModel>> SearchFileSystemEntries(
            bool? isDirectory, string? prefix = null, int maxResults = 20)
        {
            prefix = prefix?.StandardizePath();
            var results = new List<FileSystemEntryNameViewModel>();
            string? parent = null;

            if (prefix.IsNotEmpty())
            {
                if (System.IO.File.Exists(prefix))
                {
                    parent = Path.GetDirectoryName(prefix);
                    results.Add(new FileSystemEntryNameViewModel(prefix, Path.GetFileName(prefix), false));
                }
                else if (Directory.Exists(prefix))
                {
                    parent = prefix;
                    results.Add(new FileSystemEntryNameViewModel(prefix, Path.GetFileName(prefix), true));
                    prefix = null;
                }
                else
                {
                    var dir = Path.GetDirectoryName(prefix);
                    if (dir.IsNotEmpty())
                    {
                        if (Directory.Exists(dir))
                        {
                            parent = dir;
                        }
                        else
                        {
                            return new ListResponse<FileSystemEntryNameViewModel>(results);
                        }
                    }
                }
            }

            var pathSet = results.Select(r => r.Path).ToHashSet();

            if (parent == null)
            {
                var drives = DriveInfo.GetDrives()
                    .Where(d => d.IsReady)
                    .Select(d => new FileSystemEntryNameViewModel(
                        d.Name.StandardizePath()!, d.Name.StandardizePath()!, true));

                results.AddRange(drives
                    .Where(d =>
                        (prefix.IsNullOrEmpty() || d.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) &&
                        pathSet.Add(d.Path))
                    .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                    .Take(maxResults));
            }
            else
            {
                var token = HttpContext?.RequestAborted ?? CancellationToken.None;

                // Compute the user-typed leaf under the parent to leverage filesystem pattern matching
                var normalizedPrefixForName = prefix?
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var leaf = Path.GetFileName(normalizedPrefixForName);
                var pattern = string.IsNullOrEmpty(leaf) ? "*" : $"{leaf}*";

                var options = new EnumerationOptions
                {
                    RecurseSubdirectories = false,
                    IgnoreInaccessible = true,
                    MatchCasing = OperatingSystem.IsWindows() ? MatchCasing.CaseInsensitive : MatchCasing.CaseSensitive,
                };

                // Enumerate directories if requested (or when both are acceptable)
                if (isDirectory != false)
                {
                    try
                    {
                        foreach (var d in Directory.EnumerateDirectories(parent, pattern, options))
                        {
                            if (token.IsCancellationRequested) break;
                            var stdPath = d.StandardizePath()!;
                            if (pathSet.Add(stdPath))
                            {
                                results.Add(new FileSystemEntryNameViewModel(stdPath, Path.GetFileName(stdPath), true));
                            }

                            if (results.Count >= maxResults) break;
                        }
                    }
                    catch
                    {
                        // Skip enumeration failures for this parent; partial results are fine
                    }
                }

                // Enumerate files if requested (or when both are acceptable)
                if (results.Count < maxResults && isDirectory != true)
                {
                    try
                    {
                        foreach (var f in Directory.EnumerateFiles(parent, pattern, options))
                        {
                            if (token.IsCancellationRequested) break;
                            var stdPath = f.StandardizePath()!;
                            if (pathSet.Add(stdPath))
                            {
                                results.Add(new FileSystemEntryNameViewModel(f.StandardizePath()!, Path.GetFileName(f),
                                    false));
                            }

                            if (results.Count >= maxResults) break;
                        }
                    }
                    catch
                    {
                        // Skip enumeration failures
                    }
                }
            }

            return new ListResponse<FileSystemEntryNameViewModel>(results.Take(maxResults));
        }

        [HttpGet("iwfs-info")]
        [SwaggerOperation(OperationId = "GetIwFsInfo")]
        public async Task<SingletonResponse<IwFsEntryLazyInfo>> GetIwFsInfo(string path, IwFsType type)
        {
            return new SingletonResponse<IwFsEntryLazyInfo>(new IwFsEntryLazyInfo(path, type));
        }

        [HttpGet("iwfs-entry")]
        [SwaggerOperation(OperationId = "GetIwFsEntry")]
        public async Task<SingletonResponse<IwFsEntry>> GetIwFsEntry(string path)
        {
            return new SingletonResponse<IwFsEntry>(new IwFsEntry(path));
        }

        [HttpPost("directory")]
        [SwaggerOperation(OperationId = "CreateDirectory")]
        public async Task<BaseResponse> CreateDirectory(string parent)
        {
            if (!Directory.Exists(parent))
            {
                return BaseResponseBuilder.BuildBadRequest(_localizer.PathIsNotFound(parent));
            }

            var dirs = Directory.GetDirectories(parent).Select(Path.GetFileName).ToHashSet();

            for (var i = 0; i < 10000; i++)
            {
                var dirName = _localizer.NewFolderName();
                if (i > 0)
                {
                    dirName += $" ({i})";
                }

                if (!dirs.Contains(dirName))
                {
                    Directory.CreateDirectory(Path.Combine(parent, dirName));
                    return BaseResponseBuilder.Ok;
                }
            }

            return BaseResponseBuilder.SystemError;
        }

        [HttpGet("children/iwfs-info")]
        [SwaggerOperation(OperationId = "GetChildrenIwFsInfo")]
        public async Task<SingletonResponse<IwFsPreview>> Preview(string? root)
        {
            var isDirectory = false;

            root = root.StandardizePath();
            var entries = new List<IwFsEntry>();
            if (string.IsNullOrEmpty(root))
            {
                var drives = DriveInfo.GetDrives().Select(f => f.Name).ToArray();
                entries.AddRange(drives.Select(d => new IwFsEntry(d, IwFsType.Drive)));
            }
            else
            {
                if (Directory.Exists(root))
                {
                    isDirectory = true;
                }
                else
                {
                    if (!root.StartsWith(InternalOptions.UncPathPrefix))
                    {
                        if (!System.IO.File.Exists(root))
                        {
                            return SingletonResponseBuilder<IwFsPreview>.Build(ResponseCode.NotFound,
                                $"{root} does not exist");
                        }
                    }
                    else
                    {
                        isDirectory = true;
                    }
                }

                string[] dirs = [];
                string[] files = [];
                if (isDirectory)
                {
                    var dirWithPathSep = $"{root.StandardizePath()}{InternalOptions.DirSeparator}";
                    files = Directory.GetFiles(dirWithPathSep).Select(p => p.StandardizePath()!).ToArray();
                    dirs = Directory.GetDirectories(dirWithPathSep).Select(p => p.StandardizePath()!).ToArray();
                }
                else
                {
                    files = [root];
                }

                entries.AddRange(dirs.Select(d => new IwFsEntry(d, IwFsType.Directory)));
                entries.AddRange(files.Select(d => new IwFsEntry(d, IwFsType.Unknown)));
            }

            entries = entries
                // .OrderBy(t => t.Type == IwFsType.Directory ? 0 : 1)
                .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList();

            // var entriesMap = entries.ToDictionary(t => t.Path, t => t);
            // var unknownTypePaths =
            //     entriesMap.Where(k => k.Value.Type == IwFsType.Unknown).Select(t => t.Key).ToArray();
            // var compressedFileGroups = CompressedFileHelper.Group(unknownTypePaths);
            //
            // var iwFsCompressedFileGroups = new List<IwFsCompressedFileGroup>();
            //
            // foreach (var group in compressedFileGroups)
            // {
            //     for (var i = 0; i < group.Files.Count; i++)
            //     {
            //         var f = group.Files[i];
            //         var e = entriesMap[f];
            //         // e.Type = i == 0
            //         //     ? group.MissEntry ? IwFsType.CompressedFilePart : IwFsType.CompressedFileEntry
            //         //     : IwFsType.CompressedFilePart;
            //         e.Type = i == 0 ? IwFsType.CompressedFileEntry : IwFsType.CompressedFilePart;
            //         e.MeaningfulName = group.KeyName;
            //     }
            //
            //     var segments = group.Files[0].Split(BusinessConstants.PathSeparator).ToList();
            //     segments.RemoveAt(segments.Count - 1);
            //     segments.Add(group.KeyName);
            //     segments.Reverse();
            //     var passwords = segments.Select(x => x.TryGetPasswordForDecompressionExactly())
            //         .Where(a => a.IsNotEmpty()).ToArray();
            //
            //     iwFsCompressedFileGroups.Add(new IwFsCompressedFileGroup
            //     {
            //         Files = group.Files,
            //         KeyName = group.KeyName,
            //         Extension = group.Extension,
            //         Password = passwords.FirstOrDefault(),
            //         PasswordCandidates = passwords.Skip(1).ToList()
            //     });
            // }
            //
            // var allCompressedFiles = compressedFileGroups.SelectMany(a => a.Files);
            // // Make entries with other types can be decompressed.
            // var otherFiles = entries.Where(t => t.Type != IwFsType.Directory && t.Type != IwFsType.Invalid)
            //     .Select(t => t.Path).Except(allCompressedFiles).ToArray();
            // foreach (var p in otherFiles)
            // {
            //     var keyName = Path.GetFileNameWithoutExtension(p);
            //
            //     var segments = p.Split(BusinessConstants.PathSeparator).ToList();
            //     segments.RemoveAt(segments.Count - 1);
            //     segments.Add(keyName);
            //     segments.Reverse();
            //     var passwords = segments.Select(x => x.TryGetPasswordForDecompressionExactly())
            //         .Where(a => a.IsNotEmpty()).ToArray();
            //
            //     iwFsCompressedFileGroups.Add(new IwFsCompressedFileGroup
            //     {
            //         Files = new List<string> {p},
            //         KeyName = keyName,
            //         Extension = Path.GetExtension(p),
            //         Password = passwords.FirstOrDefault(),
            //         PasswordCandidates = passwords.Skip(1).ToList()
            //     });
            // }

            var rsp = new IwFsPreview()
            {
                Entries = entries.ToArray(),
            };
            return new SingletonResponse<IwFsPreview>(rsp);
        }

        [HttpDelete]
        [SwaggerOperation(OperationId = "RemoveFiles")]
        public async Task<BaseResponse> Remove([FromBody] FileRemoveRequestModel model)
        {
            var errors = new List<string>();
            foreach (var p in model.Paths.Where(e => !e.EndsWith('.')))
            {
                try
                {
                    if (System.IO.File.Exists(p))
                    {
                        FileUtils.Delete(p, false, true);
                    }
                    else
                    {
                        if (Directory.Exists(p))
                        {
                            DirectoryUtils.Delete(p, false, true);
                        }
                        else
                        {
                            throw new IOException($"{p} does not exist");
                        }
                    }
                }
                catch (Exception e)
                {
                    errors.Add($"[{p}]{e.Message}");
                }
            }

            return errors.Any()
                ? BaseResponseBuilder.Build(ResponseCode.SystemError, string.Join(Environment.NewLine, errors))
                : BaseResponseBuilder.Ok;
        }

        [HttpPut("name")]
        [SwaggerOperation(OperationId = "RenameFile")]
        public async Task<SingletonResponse<string>> Rename([FromBody] FileRenameRequestModel model)
        {
            var invalidFilenameChars = Path.GetInvalidFileNameChars();
            if (invalidFilenameChars.Any(t => model.NewName.Contains(t)))
            {
                return SingletonResponseBuilder<string>.BuildBadRequest(
                    $"File name can not contains those chars: {string.Join(',', invalidFilenameChars)}");
            }

            try
            {
                var newFullname = Path.Combine(Path.GetDirectoryName(model.Fullname)!, Path.GetFileName(model.NewName));
                if (System.IO.File.Exists(newFullname) || Directory.Exists(newFullname))
                {
                    return SingletonResponseBuilder<string>.BuildBadRequest($"Target path [{newFullname}] exists");
                }

                if (System.IO.File.Exists(model.Fullname))
                {
                    System.IO.File.Move(model.Fullname, newFullname);
                }
                else
                {
                    var dir = new DirectoryInfo(model.Fullname);
                    if (dir.Exists)
                    {
                        Directory.Move(dir.FullName, newFullname);
                    }
                    else
                    {
                        return SingletonResponseBuilder<string>.NotFound;
                    }
                }

                return new SingletonResponse<string>(newFullname);
            }
            catch (Exception e)
            {
                return SingletonResponseBuilder<string>.Build(ResponseCode.SystemError, e.Message);
            }
        }

        [HttpGet("recycle-bin")]
        [SwaggerOperation(OperationId = "OpenRecycleBin")]
        public async Task<BaseResponse> OpenRecycleBin()
        {
            Process.Start("explorer.exe", "shell:RecycleBinFolder");
            return BaseResponseBuilder.Ok;
        }

        [HttpPost("extract-and-remove-directory")]
        [SwaggerOperation(OperationId = "ExtractAndRemoveDirectory")]
        public async Task<BaseResponse> ExtractAndRemoveDirectory(string directory)
        {
            DirectoryUtils.Merge(directory, Path.GetDirectoryName(directory), false);
            return BaseResponseBuilder.Ok;
        }

        [HttpPost("move-entries")]
        [SwaggerOperation(OperationId = "MoveEntries")]
        public async Task<BaseResponse> MoveEntries([FromBody] FileMoveRequestModel model)
        {
            var paths = model.EntryPaths.FindTopLevelPaths();
            // Directory.CreateDirectory(model.DestDir);

            await _fsOptionsManager.SaveAsync(options =>
            {
                options.RecentMovingDestinations = new[] {model.DestDir}
                    .Concat(options.RecentMovingDestinations ?? []).Distinct().Take(5).ToList();
            });

            var rand = new Random();
            var taskId = $"FileSystem:BatchMove:{CryptographyUtils.Md5(string.Join('\n', paths))}";
            foreach (var path in paths)
            {
                var path1 = path;
                var targetPath = Path.Combine(model.DestDir, Path.GetFileName(path1));
                _taskManager.Enqueue(new BTaskHandlerBuilder
                {
                    GetName = () => _localizer.MoveFiles(),
                    GetMessageOnInterruption = () => _localizer.MessageOnInterruption_MoveFiles(),
                    GetDescription = () => _localizer.MoveFile(path1, targetPath),
                    ResourceType = BTaskResourceType.FileSystemEntry,
                    Type = BTaskType.MoveFiles,
                    ResourceKeys = [path],
                    Run = async args =>
                    {
                        // var fakeDelay = rand.Next(50, 300);
                        // for (var i = 0; i < 100; i++)
                        // {
                        //     await args.UpdateTask(t => t.Percentage = i + 1);
                        //     await Task.Delay(fakeDelay, args.CancellationToken);
                        // }
                        //
                        // return;

                        var isDirectory = Directory.Exists(path1);
                        var isFile = System.IO.File.Exists(path1);
                        if (isDirectory || isFile)
                        {
                            async Task ProgressChange(int p)
                            {
                                await args.UpdateTask(task => task.Percentage = p);
                            }

                            if (isDirectory)
                            {
                                await DirectoryUtils.MoveAsync(path1, targetPath, false, ProgressChange,
                                    PauseToken.None,
                                    args.CancellationToken);
                            }
                            else
                            {
                                await FileUtils.MoveAsync(path1, targetPath, false, ProgressChange, PauseToken.None,
                                    args.CancellationToken);
                            }
                        }
                    },
                    ConflictKeys = [taskId]
                });
            }
            return BaseResponseBuilder.Ok;
        }

        private static async Task<(string[] Files, string[] Directories)> _getSameNameEntriesInWorkingDirectory(
            RemoveSameEntryInWorkingDirectoryRequestModel model)
        {
            var allFiles = Directory.GetFiles(model.WorkingDir, "*.*", SearchOption.AllDirectories);
            var allDirectories = Directory.GetDirectories(model.WorkingDir, "*.*", SearchOption.AllDirectories);

            var files = new List<string>();
            var directories = new List<string>();

            foreach (var entryPath in model.EntryPaths)
            {
                var name = Path.GetFileName(entryPath);
                if (System.IO.File.Exists(entryPath))
                {
                    files.AddRange(allFiles.Where(t => t.EndsWith(name, StringComparison.OrdinalIgnoreCase)).ToArray());
                }

                if (Directory.Exists(entryPath))
                {
                    var dirs = allDirectories
                        .Where(t => t.EndsWith(name, StringComparison.OrdinalIgnoreCase)).OrderBy(t => t.Length)
                        .ThenBy(t => t).ToArray();
                    // Remove sub dirs
                    foreach (var d in dirs)
                    {
                        if (directories.Any(d.StartsWith))
                        {
                            continue;
                        }

                        directories.Add(d);
                    }
                }
            }

            return (files.ToArray(), directories.ToArray());
        }

        [HttpPost("same-name-entries-in-working-directory")]
        [SwaggerOperation(OperationId = "GetSameNameEntriesInWorkingDirectory")]
        public async Task<ListResponse<FileSystemEntryNameViewModel>> GetSameNameEntriesInWorkingDirectory(
            [FromBody] RemoveSameEntryInWorkingDirectoryRequestModel model)
        {
            model.WorkingDir = model.WorkingDir.StandardizePath()!;

            var (files, directories) = await _getSameNameEntriesInWorkingDirectory(model);

            var viewModels = files
                .Select(f =>
                    new FileSystemEntryNameViewModel(f.StandardizePath()!, f.StandardizePath()!.Replace(model.WorkingDir, null), false))
                .Concat(directories.Select(d =>
                    new FileSystemEntryNameViewModel(d.StandardizePath()!, d.StandardizePath()!.Replace(model.WorkingDir, null), true)))
                .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase);

            return new ListResponse<FileSystemEntryNameViewModel>(viewModels);
        }

        [HttpDelete("same-name-entry-in-working-directory")]
        [SwaggerOperation(OperationId = "RemoveSameNameEntryInWorkingDirectory")]
        public async Task<BaseResponse> RemoveSameNameEntryInWorkingDirectory(
            [FromBody] RemoveSameEntryInWorkingDirectoryRequestModel model)
        {
            var paths = await _getSameNameEntriesInWorkingDirectory(model);
            foreach (var be in paths.Files)
            {
                FileUtils.Delete(be, false, true);
            }

            foreach (var be in paths.Directories)
            {
                DirectoryUtils.Delete(be, false, true);
            }


            return BaseResponseBuilder.Ok;
        }

        [HttpPut("standardize")]
        [SwaggerOperation(OperationId = "StandardizeEntryName")]
        public async Task<BaseResponse> StandardizeEntryName(string path)
        {
            var filename = Path.GetFileName(path);
            var newName = await _specialTextService.Pretreatment(filename);
            if (filename == newName)
            {
                return BaseResponseBuilder.NotModified;
            }

            var newFullname = Path.Combine(Path.GetDirectoryName(path), newName);
            if (System.IO.File.Exists(newFullname) || Directory.Exists(newFullname))
            {
                return BaseResponseBuilder.BuildBadRequest($"New entry path {newFullname} exists");
            }

            Directory.Move(path, newFullname);
            return BaseResponseBuilder.Ok;
        }

        [HttpGet("play")]
        [SwaggerOperation(OperationId = "PlayFile")]
        public async Task<IActionResult> Play(string fullname)
        {
            var ext = Path.GetExtension(fullname);
            if (InternalOptions.ImageExtensions.Contains(ext) || InternalOptions.VideoExtensions.Contains(ext) ||
                InternalOptions.AudioExtensions.Contains(ext) || InternalOptions.TextExtensions.Contains(ext))
            {
                if (fullname.Contains(InternalOptions.CompressedFileRootSeparator))
                {
                    var tmpSegments = fullname.Split(InternalOptions.CompressedFileRootSeparator);
                    var segments = new List<string>();
                    for (var i = 0; i < tmpSegments.Length; i++)
                    {
                        var s = tmpSegments[i];
                        if (InternalOptions.CompressedFileExtensions.Any(e =>
                                s.EndsWith(e, StringComparison.OrdinalIgnoreCase)))
                        {
                            segments.Add(s);
                        }
                        else
                        {
                            var combined = $"{s}{InternalOptions.CompressedFileRootSeparator}";
                            if (i < tmpSegments.Length - 1)
                            {
                                combined += tmpSegments[i + 1];
                            }

                            i++;
                            segments.Add(combined);
                        }
                    }

                    if (segments.Count == 2)
                    {
                        var compressFilePath = segments[0];
                        var entryPath = segments[1];

                        var stream = await _compressedFileService.ExtractOneEntry(compressFilePath, entryPath,
                            HttpContext.RequestAborted);

                        if (stream != null)
                        {
                            return File(stream, MimeTypes.GetMimeType(fullname));
                        }

                        return NotFound();
                    }

                    if (segments.Count == tmpSegments.Length)
                    {
                        return NotFound();
                    }
                }

                // Handle video transcoding if needed
                if (InternalOptions.VideoExtensions.Contains(ext))
                {
                    var ffprobePath = _ffMpegService.FfProbeExecutable;
                    var ffprobeResult = await Cli.Wrap(ffprobePath)
                        .WithArguments(args =>
                        {
                            args
                                .Add("-v").Add("error")
                                .Add("-select_streams").Add("v:0")
                                .Add("-show_entries").Add("stream=codec_name")
                                .Add("-of").Add("default=noprint_wrappers=1:nokey=1")
                                .Add(fullname);
                        })
                        .WithValidation(CommandResultValidation.None)
                        .ExecuteBufferedAsync(HttpContext.RequestAborted);
                    var codecName = ffprobeResult.StandardOutput.Trim();

                    // If video is already h264, stream directly
                    if (codecName.ToLower() == "h264")
                    {
                        var stream = new FileStream(fullname, FileMode.Open, FileAccess.Read, FileShare.Read);
                        HttpContext.RequestAborted.Register(() => stream.Dispose());
                        return File(stream, "video/mp4", Path.GetFileName(fullname));
                    }
                    else
                    {
                        // Get hardware acceleration info (cached)
                        var hwAccelInfo = await _hardwareAccelerationService.GetHardwareAccelerationInfoAsync(HttpContext.RequestAborted);
                        var preferredCodec = hwAccelInfo.PreferredCodec;
                        
                        _logger.LogInformation("Using codec {Codec} for video transcoding of {FileName}", preferredCodec, Path.GetFileName(fullname));
                        
                        // Transcode to h264 using ffmpeg with hardware acceleration if available
                        var ffmpegPath = _ffMpegService.FfMpegExecutable;
                        var ffmpegCmd = Cli.Wrap(ffmpegPath)
                            .WithArguments(args =>
                            {
                                args
                                    .Add("-i").Add(fullname)
                                    .Add("-c:v").Add(preferredCodec);
                                
                                // Add hardware-specific options based on the codec
                                if (preferredCodec == "h264_nvenc")
                                {
                                    args
                                        .Add("-preset").Add("p1") // NVENC preset
                                        .Add("-tune").Add("hq"); // High quality tuning
                                }
                                else if (preferredCodec == "h264_qsv")
                                {
                                    args
                                        .Add("-preset").Add("veryfast") // QSV preset
                                        .Add("-look_ahead").Add("1"); // Enable look-ahead
                                }
                                else if (preferredCodec == "h264_amf")
                                {
                                    args
                                        .Add("-quality").Add("speed") // AMF quality preset
                                        .Add("-rc").Add("cqp"); // Rate control
                                }
                                else if (preferredCodec == "h264_videotoolbox")
                                {
                                    args
                                        .Add("-allow_sw").Add("1"); // Allow software fallback
                                }
                                else
                                {
                                    // Software encoding (libx264)
                                    args
                                        .Add("-preset").Add("ultrafast");
                                }
                                
                                args
                                    .Add("-b:v").Add("3M")
                                    .Add("-maxrate").Add("4M")
                                    .Add("-bufsize").Add("6M")
                                    .Add("-vf").Add("scale=min(1920\\,iw):min(1080\\,ih),fps=60")
                                    .Add("-c:a").Add("aac")
                                    .Add("-f").Add("mp4")
                                    .Add("-movflags").Add("frag_keyframe+empty_moov")
                                    .Add("pipe:1");
                            })
                            .WithStandardOutputPipe(PipeTarget.ToStream(Response.Body, true));
                        
                        // Set response headers for streaming mp4
                        Response.ContentType = "video/mp4";
                        Response.Headers["Content-Disposition"] = $"inline; filename=\"{Path.GetFileName(fullname)}\"";
                        await ffmpegCmd.ExecuteAsync(HttpContext.RequestAborted);
                        return new EmptyResult();
                    }
                }
                else
                {
                    // For images, audio, text: stream directly
                    var mimeType = MimeTypes.GetMimeType(ext);
                    var fs = System.IO.File.OpenRead(fullname);
                    return File(fs, mimeType, true);
                }
            }

            return StatusCode((int)HttpStatusCode.UnsupportedMediaType);
        }

        [HttpPost("decompression")]
        [SwaggerOperation(OperationId = "DecompressFiles")]
        public async Task<BaseResponse> DecompressFiles([FromBody] FileDecompressRequestModel model)
        {
            var allPaths = model.Paths.ToHashSet();
            var parentsMappings = allPaths.GroupBy(Path.GetDirectoryName).ToDictionary(t => t.Key!, t => t.ToList())
                .ToList();
            for (var i = 0; i < parentsMappings.Count; i++)
            {
                var (parent, paths) = parentsMappings[i];
                var dirInfo = new DirectoryInfo(parent!);
                if (dirInfo.Exists)
                {
                    var groups = CompressedFileHelper.Group(dirInfo.GetFiles().Select(t => t.FullName).ToArray())
                        .Where(t => t.Files.Any(paths.Contains)).ToList();
                    var compressedFiles = groups.SelectMany(t => t.Files).ToArray();
                    var otherFiles = paths.Except(compressedFiles).ToArray();
                    var otherGroups = otherFiles
                        .Select(CompressedFileHelper.CompressedFileGroup.FromSingleFile<IwFsCompressedFileGroup>)
                        .ToArray();

                    var allGroups = groups.Concat(otherGroups).Select(g =>
                    {
                        var password = model.Password ??
                                       (g.KeyName.GetPasswordsFromPathWithoutExtension().FirstOrDefault() ??
                                        parent.GetPasswordsFromPathWithoutExtension().FirstOrDefault());
                        return new IwFsCompressedFileGroup
                        {
                            Extension = g.Extension,
                            Files = g.Files,
                            KeyName = g.KeyName,
                            Password = password
                        };
                    }).ToList();

                    var rand = new Random();

                    for (var j = 0; j < allGroups.Count; j++)
                    {
                        var group = allGroups[j];
                        var entry = group.Files.First();
                        if (group.Files.Count == 1 && Path.GetExtension(entry).IsNullOrEmpty())
                        {
                            const string ext = ".iw-decompressing";
                            var newFile = $"{entry}{ext}";
                            System.IO.File.Move(entry, newFile);
                            group.Files[0] = newFile;
                            entry = newFile;
                        }

                        var taskId = IwFsCompressedFile.BuildDecompressionTaskName(entry);
                        if (_taskManager.IsPending(taskId))
                        {
                            continue;
                        }

                        var files = group.Files.ToArray();

                        _taskManager.Enqueue(new BTaskHandlerBuilder
                        {
                            ResourceType = BTaskResourceType.FileSystemEntry,
                            ConflictKeys = [taskId],
                            GetDescription = () => taskId,
                            GetName = () => _localizer.Decompress(),
                            Type = BTaskType.Decompress,
                            ResourceKeys = files.Cast<object>().ToArray(),
                            Run = async args =>
                            {
                                await using var scope = args.RootServiceProvider.CreateAsyncScope();

                                if (group.Password.IsNotEmpty())
                                {
                                    var passwordService = scope.ServiceProvider.GetRequiredService<PasswordService>();
                                    await passwordService.AddUsedTimes(group.Password!);
                                }


                                // var fakeDelay = rand.Next(50, 250);
                                // for (var i = 0; i < 100; i++)
                                // {
                                //     await args.UpdateTask(t => t.Percentage = i + 1);
                                //     await Task.Delay(fakeDelay, args.CancellationToken);
                                // }
                                //
                                // return;

                                var osb = new StringBuilder();
                                var esb = new StringBuilder();
                                var workingDir = Path.GetDirectoryName(entry);
                                var targetDir = Path.Combine(workingDir,
                                    group.KeyName);
                                var processRegex = new Regex(@$"\d+\%");
                                var tryPSwitch = false;

                                var messageSb = new StringBuilder();

                                BuildCommand:
                                var command = Cli.Wrap(_sevenZExecutable)
                                    .WithWorkingDirectory(workingDir)
                                    .WithValidation(CommandResultValidation.None)
                                    .WithArguments(new[]
                                        {
                                            "x",
                                            entry,
                                            tryPSwitch
                                                ? $"-p{group.Password}"
                                                : null,
                                            tryPSwitch
                                                ? $"-aos"
                                                : null,
                                            $"-o{targetDir}",
                                            // redirect process to stderr stream
                                            "-sccUTF-8",
                                            "-scsUTF-16LE",
                                            "-bsp2"
                                        }.Where(t => t.IsNotEmpty())!,
                                        true)
                                    .WithStandardErrorPipe(PipeTarget.Merge(PipeTarget.ToDelegate(async line =>
                                            {
                                                var match = processRegex.Match(line);
                                                if (match.Success)
                                                {
                                                    var np = int.Parse(match.Value.TrimEnd('%'));
                                                    if (np != args.Task.Percentage)
                                                    {
                                                        await args.UpdateTask(x => x.Percentage = np);
                                                    }
                                                }
                                            },
                                            Encoding.UTF8),
                                        PipeTarget.ToStringBuilder(esb,
                                            Encoding.UTF8)))
                                    .WithStandardOutputPipe(PipeTarget.ToStringBuilder(osb,
                                        Encoding.UTF8));
                                // Input password via stdin
                                if (group.Password.IsNotEmpty() && !tryPSwitch)
                                {
                                    command = command.WithStandardInputPipe(
                                        PipeSource.FromString(group.Password!,
                                            Encoding.UTF8));
                                }

                                var result = await command.ExecuteAsync();
                                if (result.ExitCode == 0)
                                {
                                    return;
                                }

                                messageSb.AppendLine($"Decompression exit with code: {result.ExitCode}");
                                if (osb.Length > 0)
                                {
                                    messageSb.AppendLine(osb.ToString());
                                }

                                if (esb.Length > 0)
                                {
                                    messageSb.AppendLine(esb.ToString());
                                }

                                messageSb.AppendLine($"Command: {command}");

                                var message = messageSb.ToString();
                                var wrongPassword = message.Contains("Wrong password");

                                if (wrongPassword)
                                {
                                    messageSb.AppendLine(
                                        "Wrong password error occurred, cleaning bad files(0kb, etc).");
                                    // Clean empty files
                                    // ERROR: Wrong password : 实际异常-原结果[未检测]-正常-fa1abb4093754661b3931c03d3f1a72a-vibration-2.wav
                                    const string prefix = "ERROR: Wrong password : ";
                                    var badFiles = message.Split('\n',
                                            '\r')
                                        .Where(a => a.StartsWith(prefix))
                                        .Select(t => Path.Combine(targetDir,
                                            t.Replace(prefix,
                                                    null)
                                                .Trim()))
                                        .ToArray();
                                    foreach (var f in badFiles)
                                    {
                                        var fi = new FileInfo(f);
                                        if (fi.Exists && fi.Length == 0)
                                        {
                                            fi.Delete();
                                            messageSb.AppendLine($"Deleting {fi.FullName}");
                                        }
                                    }

                                    // Since we've got directories from paths of files, so we must populate the paths of directories between the files and the targetDir
                                    var directories = badFiles.Select(Path.GetDirectoryName)
                                        .Distinct()
                                        .SelectMany(dir =>
                                        {
                                            var chain = dir.Replace(targetDir,
                                                    null)
                                                .Split(Path.DirectorySeparatorChar,
                                                    Path.AltDirectorySeparatorChar)
                                                .Where(a => a.IsNotEmpty())
                                                .ToArray();
                                            return chain.Select((t1,
                                                        i) =>
                                                    Path.Combine(new[]
                                                        {
                                                            targetDir
                                                        }.Concat(chain.Take(i + 1))
                                                        .ToArray()))
                                                .ToList();
                                        })
                                        .ToList();
                                    directories.Add(targetDir);
                                    directories = directories.OrderByDescending(t => t.Length)
                                        .Distinct()
                                        .ToList();
                                    foreach (var d in directories)
                                    {
                                        var di = new DirectoryInfo(d);
                                        if (di.Exists &&
                                            di.GetFileSystemInfos()
                                                .Length ==
                                            0)
                                        {
                                            di.Delete();
                                            messageSb.AppendLine($"Deleting {di.FullName}");
                                        }
                                    }

                                    // Try -p switch
                                    if (!tryPSwitch && group.Password.IsNotEmpty())
                                    {
                                        tryPSwitch = true;
                                        messageSb.AppendLine("Try to use -p switch, re-decompressing");
                                        esb.Clear();
                                        osb.Clear();
                                        goto BuildCommand;
                                    }

                                    throw new BTaskException(_localizer.WrongPassword(), messageSb.ToString());
                                }

                                throw new BTaskException(null, messageSb.ToString());
                            }
                        });
                    }
                }
            }

            return BaseResponseBuilder.Ok;
        }

        private static readonly string PngContentType = MimeTypes.GetMimeType(".png");

        [HttpGet("icon")]
        [SwaggerOperation(OperationId = "GetIconData")]
        public Task<SingletonResponse<string>> GetIcon(IconType type, string? path)
        {
            string? iconBase64 = null;
            var icon = _guiAdapter.GetIcon(type, path);
            if (icon != null)
            {
                iconBase64 = $@"data:{PngContentType};base64," + Convert.ToBase64String(icon);
            }

            return Task.FromResult(new SingletonResponse<string>(iconBase64));
        }

        [HttpGet("all-files")]
        [SwaggerOperation(OperationId = "GetAllFiles")]
        public async Task<ListResponse<string>> GetAllFiles(string path)
        {
            if (System.IO.File.Exists(path))
            {
                return new ListResponse<string>(new[] {path});
            }

            if (!Directory.Exists(path))
            {
                return new ListResponse<string>();
            }

            return new ListResponse<string>(Directory.GetFiles(path, "*.*", SearchOption.AllDirectories)
                .Select(a => a.StandardizePath()!));
        }

        [HttpGet("compressed-file/entries")]
        [SwaggerOperation(OperationId = "GetCompressedFileEntries")]
        public async Task<ListResponse<CompressedFileEntry>> GetCompressFileEntries(string compressedFilePath)
        {
            var sw = Stopwatch.StartNew();
            var rsp = await _compressedFileService.GetCompressFileEntries(compressedFilePath,
                HttpContext.RequestAborted);
            sw.Stop();
            _logger.LogInformation(sw.ElapsedMilliseconds + "ms");
            return rsp;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sampleFile">Get extensions of files with same path layer of <see cref="sampleFile"/></param>
        /// <param name="rootPath"></param>
        /// <returns></returns>
        [HttpGet("file-extension-counts")]
        [SwaggerOperation(OperationId = "GetFileExtensionCounts")]
        public async Task<SingletonResponse<Dictionary<string, int>>> GetFileExtensionCounts(string sampleFile,
            string rootPath)
        {
            var startLayer = rootPath.StandardizePath()!.SplitPathIntoSegments().Length - 1;
            var sameLayerFiles = FileUtils.GetSameLayerFiles(sampleFile, startLayer);
            var extensions = sameLayerFiles.Select(Path.GetExtension).Where(a => a.IsNotEmpty()).GroupBy(a => a)
                .ToDictionary(a => a.Key, a => a.Count());
            return new SingletonResponse<Dictionary<string, int>>(extensions);
        }

        [HttpPut("group-preview")]
        [SwaggerOperation(OperationId = "PreviewFileSystemEntriesGroupResult")]
        public async Task<ListResponse<FileSystemEntryGroupResultViewModel>> GroupPreview(
            [FromBody] FileSystemEntryGroupInputModel model)
        {
            if (model.Paths.Length == 0)
            {
                return ListResponseBuilder<FileSystemEntryGroupResultViewModel>.NotFound;
            }

            var batches = new Dictionary<string, List<string>>();

            if (model.GroupInternal)
            {
                foreach (var rootPath in model.Paths)
                {
                    if (System.IO.File.Exists(rootPath))
                    {
                        continue;
                    }

                    if (!Directory.Exists(rootPath))
                    {
                        return ListResponseBuilder<FileSystemEntryGroupResultViewModel>.NotFound;
                    }

                    batches[rootPath] = Directory.GetFiles(rootPath).ToList();
                }
            }
            else
            {
                foreach (var pg in model.Paths.GroupBy(Path.GetDirectoryName))
                {
                    if (pg.Key.IsNotEmpty())
                    {
                        foreach (var path in pg)
                        {
                            if (System.IO.File.Exists(path))
                            {
                                batches.GetOrAdd(pg.Key, _ => []).Add(path);
                            }
                        }
                    }
                }
            }

            if (!batches.Any())
            {
                return ListResponseBuilder<FileSystemEntryGroupResultViewModel>.NotFound;
            }

            var vms = batches.Select(g =>
            {
                var rootPath = g.Key.StandardizePath()!;
                return new FileSystemEntryGroupResultViewModel
                {
                    RootPath = rootPath,
                    Groups = g.Value.GroupBy(Path.GetFileNameWithoutExtension)
                        .Where(x => x.Key.IsNotEmpty()).Select(x =>
                            new FileSystemEntryGroupResultViewModel.GroupViewModel
                            {
                                DirectoryName = x.Key.StandardizePath()!,
                                Filenames = x.Select(y => Path.GetFileName(y)).ToArray()
                            }).ToArray()
                };
            });

            return new ListResponse<FileSystemEntryGroupResultViewModel>(vms);
        }

        [HttpPut("group")]
        [SwaggerOperation(OperationId = "MergeFileSystemEntries")]
        public async Task<BaseResponse> Group([FromBody] FileSystemEntryGroupInputModel model)
        {
            var r = await GroupPreview(model);
            if (r.Code != 0)
            {
                return r;
            }

            var batches = r.Data!;

            foreach (var batch in batches)
            {
                foreach (var group in batch.Groups)
                {
                    var dirFullname = Path.Combine(batch.RootPath, group.DirectoryName);
                    Directory.CreateDirectory(dirFullname);
                    foreach (var f in group.Filenames)
                    {
                        var sourceFile = Path.Combine(batch.RootPath, f);
                        var fileFullname = Path.Combine(dirFullname, f);
                        // Use quickly way in same drive
                        System.IO.File.Move(sourceFile, fileFullname, false);
                    }
                }
            }

            return BaseResponseBuilder.Ok;
        }

        [HttpPost("file-processor-watcher")]
        [SwaggerOperation(OperationId = "StartWatchingChangesInFileProcessorWorkspace")]
        public async Task<BaseResponse> StartWatchingChangesInFileProcessorWorkspace(string path)
        {
            _fileProcessorWatcher.Start(path);
            return BaseResponseBuilder.Ok;
        }

        [HttpDelete("file-processor-watcher")]
        [SwaggerOperation(OperationId = "StopWatchingChangesInFileProcessorWorkspace")]
        public async Task<BaseResponse> StopWatchingChangesInFileProcessorWorkspace()
        {
            _fileProcessorWatcher.Stop();
            return BaseResponseBuilder.Ok;
        }

        [HttpGet("is-file")]
        [SwaggerOperation(OperationId = "CheckPathIsFile")]
        public async Task<SingletonResponse<bool>> CheckPathIsFile(string path)
        {
            return new SingletonResponse<bool>(System.IO.File.Exists(path));
        }

        [HttpGet("hardware-acceleration")]
        [SwaggerOperation(OperationId = "GetHardwareAccelerationInfo")]
        public async Task<SingletonResponse<HardwareAccelerationInfo>> GetHardwareAccelerationInfo()
        {
            var info = await _hardwareAccelerationService.GetHardwareAccelerationInfoAsync(HttpContext.RequestAborted);
            return new SingletonResponse<HardwareAccelerationInfo>(info);
        }

        [HttpPost("hardware-acceleration/clear-cache")]
        [SwaggerOperation(OperationId = "ClearHardwareAccelerationCache")]
        public async Task<BaseResponse> ClearHardwareAccelerationCache()
        {
            _hardwareAccelerationService.ClearCache();
            return BaseResponseBuilder.Ok;
        }
    }
}