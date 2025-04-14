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
using Bakabase.InsideWorld.Business.Components.FileExplorer;
using Bakabase.InsideWorld.Business.Components.FileExplorer.Entries;
using Bakabase.InsideWorld.Business.Components.FileExplorer.Information;
using Bakabase.InsideWorld.Business.Configurations;
using Bakabase.InsideWorld.Business.Extensions;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.InsideWorld.Models.Models.Aos;
using Bakabase.InsideWorld.Models.RequestModels;
using Bakabase.Service.Models.Input;
using Bakabase.Service.Models.View;
using Bakabase.Service.Models.View.Constants;
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Components.Cryptography;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Components.Storage;
using Bootstrap.Components.Tasks;
using Bootstrap.Extensions;
using Bootstrap.Models.Constants;
using Bootstrap.Models.ResponseModels;
using CliWrap;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers
{
    [Route("~/file")]
    public class FileController : Controller
    {
        private readonly ISpecialTextService _specialTextService;
        private readonly IWebHostEnvironment _env;
        private readonly BTaskManager _taskManager;
        private readonly InsideWorldOptionsManagerPool _insideWorldOptionsManager;
        private readonly string _sevenZExecutable;
        private readonly CompressedFileService _compressedFileService;
        private readonly IBOptionsManager<FileSystemOptions> _fsOptionsManager;
        private readonly InsideWorldLocalizer _localizer;
        private readonly IwFsWatcher _fileProcessorWatcher;
        private readonly PasswordService _passwordService;
        private readonly ILogger<FileController> _logger;
        private readonly IGuiAdapter _guiAdapter;

        public FileController(ISpecialTextService specialTextService, IWebHostEnvironment env,
            InsideWorldOptionsManagerPool insideWorldOptionsManager,
            CompressedFileService compressedFileService, IBOptionsManager<FileSystemOptions> fsOptionsManager,
            IwFsWatcher fileProcessorWatcher, PasswordService passwordService, ILogger<FileController> logger,
            InsideWorldLocalizer localizer, BTaskManager taskManager, IGuiAdapter guiAdapter)
        {
            _specialTextService = specialTextService;
            _env = env;
            _insideWorldOptionsManager = insideWorldOptionsManager;
            _compressedFileService = compressedFileService;
            _fsOptionsManager = fsOptionsManager;
            _fileProcessorWatcher = fileProcessorWatcher;
            _passwordService = passwordService;
            _logger = logger;
            _localizer = localizer;
            _taskManager = taskManager;
            _guiAdapter = guiAdapter;

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

        [HttpGet("iwfs-info")]
        [SwaggerOperation(OperationId = "GetIwFsInfo")]
        public async Task<SingletonResponse<IwFsEntryLazyInfo>> GetIwFsInfo(string path)
        {
            return new SingletonResponse<IwFsEntryLazyInfo>(new IwFsEntryLazyInfo
            {
                ChildrenCount = Directory.GetFileSystemEntries(path).Length
            });
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
            string[] files;

            root = root.StandardizePath();
            if (string.IsNullOrEmpty(root))
            {
                files = DriveInfo.GetDrives().Select(f => f.Name).ToArray();
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

                if (isDirectory)
                {
                    var dirWithPathSep = $"{root.StandardizePath()}{InternalOptions.DirSeparator}";
                    files = Directory.GetFileSystemEntries(dirWithPathSep).Select(p => p.StandardizePath()!).ToArray();
                }
                else
                {
                    files = new[] {root};
                }
            }

            var entries = files.AsParallel().Select(t => new IwFsEntry(t))
                // .OrderBy(t => t.Type == IwFsType.Directory ? 0 : 1)
                .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToArray();

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

            var fileChain = new List<IwFsEntry> { };
            var pathSegments = root?
                .Split(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
                .Where(t => t.IsNotEmpty()).ToArray() ?? Array.Empty<string>();
            string? lastFileChainPath = null;
            foreach (var s in pathSegments.Take(isDirectory ? pathSegments.Length : pathSegments.Length - 1))
            {
                lastFileChainPath = lastFileChainPath.IsNullOrEmpty() ? s : Path.Combine(lastFileChainPath!, s);

                fileChain.Add(new IwFsEntry
                {
                    Path = lastFileChainPath,
                    Name = s
                });
            }

            var rsp = new IwFsPreview()
            {
                Entries = entries,
                DirectoryChain = fileChain.ToArray(),
                // CompressedFileGroups = iwFsCompressedFileGroups.ToArray()
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

            await _insideWorldOptionsManager.FileSystem.SaveAsync(options =>
            {
                options.RecentMovingDestinations = new[] {model.DestDir}
                    .Concat(options.RecentMovingDestinations ?? []).Distinct().Take(5).ToArray();
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

                var mimeType = MimeTypes.GetMimeType(ext);
                var fs = System.IO.File.OpenRead(fullname);
                return File(fs, mimeType, true);
            }

            return StatusCode((int) HttpStatusCode.UnsupportedMediaType);
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
        public async Task<SingletonResponse<FileSystemEntryGroupResultViewModel>> GroupPreview(
            [FromBody] FileSystemEntryGroupInputModel model)
        {
            if (model.Paths.Length == 0)
            {
                return SingletonResponseBuilder<FileSystemEntryGroupResultViewModel>.NotFound;
            }

            var files = new List<string>();

            string rootPath;

            if (model.GroupInternal)
            {
                if (model.Paths.Length > 1)
                {
                    return SingletonResponseBuilder<FileSystemEntryGroupResultViewModel>.BuildBadRequest(
                        _localizer.PathsShouldBeInSameDirectory());
                }

                rootPath = model.Paths[0];

                if (!Directory.Exists(rootPath))
                {
                    return SingletonResponseBuilder<FileSystemEntryGroupResultViewModel>.NotFound;
                }

                files.AddRange(Directory.GetFiles(rootPath));
            }
            else
            {
                if (model.Paths.GroupBy(Path.GetDirectoryName, StringComparer.OrdinalIgnoreCase).Count() > 1)
                {
                    return SingletonResponseBuilder<FileSystemEntryGroupResultViewModel>.BuildBadRequest(
                        _localizer.PathsShouldBeInSameDirectory());
                }

                rootPath = Path.GetDirectoryName(model.Paths[0])!;

                foreach (var path in model.Paths)
                {
                    if (System.IO.File.Exists(path))
                    {
                        files.Add(path);
                    }
                }
            }

            rootPath = rootPath.StandardizePath()!;

            var vm = new FileSystemEntryGroupResultViewModel
            {
                RootPath = rootPath,
                Groups = files.GroupBy(d => Path.GetFileNameWithoutExtension(d) ?? string.Empty,
                        StringComparer.OrdinalIgnoreCase)
                    .Where(x => x.Key.IsNotEmpty()).Select(x => new FileSystemEntryGroupResultViewModel.GroupViewModel
                    {
                        DirectoryName = x.Key,
                        Filenames = x.Select(y => Path.GetFileName(y)!).ToArray()
                    }).ToArray()
            };

            return new SingletonResponse<FileSystemEntryGroupResultViewModel>(vm);
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

            var result = r.Data!;

            foreach (var group in result.Groups)
            {
                var dirFullname = Path.Combine(result.RootPath, group.DirectoryName);
                Directory.CreateDirectory(dirFullname);
                foreach (var f in group.Filenames)
                {
                    var sourceFile = Path.Combine(result.RootPath, f);
                    var fileFullname = Path.Combine(dirFullname, f);
                    // Use quickly way in same drive
                    System.IO.File.Move(sourceFile, fileFullname, false);
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
            var isFile = false;
            try
            {
                isFile = System.IO.File.Exists(path);
            }
            catch (Exception e)
            {
                // ignored
            }

            return new SingletonResponse<bool>(isFile);
        }
    }
}