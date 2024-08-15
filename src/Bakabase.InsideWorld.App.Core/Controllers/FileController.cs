using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components;
using Bakabase.InsideWorld.Business.Components.Compression;
using Bakabase.InsideWorld.Business.Components.FileExplorer;
using Bakabase.InsideWorld.Business.Components.FileExplorer.Entries;
using Bakabase.InsideWorld.Business.Components.FileExplorer.Information;
using Bakabase.InsideWorld.Business.Components.Tasks;
using Bakabase.InsideWorld.Business.Configurations;
using Bakabase.InsideWorld.Business.Extensions;
using Bakabase.InsideWorld.Business.Resources;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Extensions;
using Bakabase.InsideWorld.Models.Models.Aos;
using Bakabase.InsideWorld.Models.RequestModels;
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Components.Storage;
using Bootstrap.Extensions;
using Bootstrap.Models.Constants;
using Bootstrap.Models.ResponseModels;
using CliWrap;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.InsideWorld.App.Core.Controllers
{
    [Route("~/file")]
    public class FileController : Controller
    {
        private readonly ISpecialTextService _specialTextService;
        private readonly IWebHostEnvironment _env;
        private readonly BackgroundTaskManager _backgroundTaskManager;
        private readonly BackgroundTaskHelper _backgroundTaskHelper;
        private readonly IwFsEntryTaskManager _iwFsEntryTaskManager;
        private readonly InsideWorldOptionsManagerPool _insideWorldOptionsManager;
        private readonly string _sevenZExecutable;
        private readonly CompressedFileService _compressedFileService;
        private readonly IBOptionsManager<FileSystemOptions> _fsOptionsManager;
        private readonly InsideWorldLocalizer _localizer;
        private readonly IwFsWatcher _fileProcessorWatcher;
        private readonly PasswordService _passwordService;
        private readonly ILogger<FileController> _logger;

        public FileController(ISpecialTextService specialTextService, IWebHostEnvironment env,
            BackgroundTaskManager backgroundTaskManager, IwFsEntryTaskManager iwFsEntryTaskManager,
            BackgroundTaskHelper backgroundTaskHelper, InsideWorldOptionsManagerPool insideWorldOptionsManager,
            CompressedFileService compressedFileService, IBOptionsManager<FileSystemOptions> fsOptionsManager,
            IwFsWatcher fileProcessorWatcher, PasswordService passwordService, ILogger<FileController> logger, InsideWorldLocalizer localizer)
        {
            _specialTextService = specialTextService;
            _env = env;
            _backgroundTaskManager = backgroundTaskManager;
            _iwFsEntryTaskManager = iwFsEntryTaskManager;
            _backgroundTaskHelper = backgroundTaskHelper;
            _insideWorldOptionsManager = insideWorldOptionsManager;
            _compressedFileService = compressedFileService;
            _fsOptionsManager = fsOptionsManager;
            _fileProcessorWatcher = fileProcessorWatcher;
            _passwordService = passwordService;
            _logger = logger;
            _localizer = localizer;

            _sevenZExecutable = Path.Combine(_env.ContentRootPath, "libs/7z.exe");
        }

        [HttpGet("task-info")]
        [SwaggerOperation(OperationId = "GetEntryTaskInfo")]
        public async Task<SingletonResponse<IwFsTaskInfo>> GetEntryTaskInfo(string path)
        {
            return new SingletonResponse<IwFsTaskInfo>(_iwFsEntryTaskManager.Get(path));
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
                            return SingletonResponseBuilder<IwFsPreview>.Build(ResponseCode.NotFound, $"{root} does not exist");
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
                    files = new[] { root };
                }
            }

            var entries = files.AsParallel().Select(t => new IwFsEntry(t)).OrderBy(t => t.Type == IwFsType.Directory ? 0 : 1)
                .ThenBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();

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
            // var parent = Path.GetDirectoryName(directory);
            // var files = Directory.GetFiles(directory, "*.*", SearchOption.AllDirectories);
            // var newFiles = files.Select(t => t.Replace(directory, parent)).ToArray();
            // var existedFiles = newFiles.Where(System.IO.File.Exists).ToArray();
            // if (existedFiles.Any())
            // {
            //     return BaseResponseBuilder.BuildBadRequest(
            //         $"File exists: {Environment.NewLine}{string.Join(Environment.NewLine, existedFiles)}");
            // }

            DirectoryUtils.Merge(directory, Path.GetDirectoryName(directory), false);

            return BaseResponseBuilder.Ok;
        }

        [HttpPost("move-entries")]
        [SwaggerOperation(OperationId = "MoveEntries")]
        public async Task<BaseResponse> MoveEntries([FromBody] FileMoveRequestModel model)
        {
            var paths = model.EntryPaths.FindTopLevelPaths();
            Directory.CreateDirectory(model.DestDir);

            await _insideWorldOptionsManager.FileSystem.SaveAsync(options =>
            {
                options.RecentMovingDestinations = new[] {model.DestDir}
                    .Concat(options.RecentMovingDestinations ?? Array.Empty<string>()).Distinct().Take(5).ToArray();
            });

            var taskName = $"FileSystem:BatchMove:{DateTime.Now:HH:mm:ss}";
            _backgroundTaskManager.RunInBackground(taskName, new CancellationTokenSource(), async (bt, sp) =>
                {
                    var unitEntryPercentage = (decimal) 1 / paths.Length;
                    for (var i = 0; i < paths.Length; i++)
                    {
                        var path = paths[i];
                        if (Directory.Exists(path) || System.IO.File.Exists(path))
                        {
                            await _iwFsEntryTaskManager.Add(new IwFsTaskInfo(path, IwFsEntryTaskType.Moving, bt.Id,
                                $"{IwFsEntryTaskType.Moving} to {model.DestDir}"));

                            var i1 = i;
                            try
                            {
                                await DirectoryUtils.MoveAsync(path, model.DestDir, false, async p =>
                                {
                                    await _iwFsEntryTaskManager.Update(path, t => t.Percentage = p);
                                    var totalPercentage = (int) (unitEntryPercentage * (i1 + (decimal) p / 100));
                                    bt.Percentage = totalPercentage;
                                }, bt.Cts.Token);
                                await _iwFsEntryTaskManager.Clear(path);
                            }
                            catch (Exception e)
                            {
                                await _iwFsEntryTaskManager.Update(path, t => t.Error = e.BuildFullInformationText());
                            }
                        }
                    }

                    return BaseResponseBuilder.Ok;
                }, BackgroundTaskLevel.Default, null, null,
                async task => { await _iwFsEntryTaskManager.Update(paths, t => t.Error = task.Message); });

            return BaseResponseBuilder.Ok;
        }

        private static async Task<string[]> _getSameNameEntriesInWorkingDirectory(
            RemoveSameEntryInWorkingDirectoryRequestModel model)
        {
            var name = Path.GetFileName(model.EntryPath);
            if (System.IO.File.Exists(model.EntryPath))
            {
                var files = Directory.GetFiles(model.WorkingDir, "*.*", SearchOption.AllDirectories)
                    .Where(t => t.EndsWith(name, StringComparison.OrdinalIgnoreCase)).ToArray();
                return files;
            }

            if (Directory.Exists(model.EntryPath))
            {
                var dirs = Directory.GetDirectories(model.WorkingDir, "*.*", SearchOption.AllDirectories)
                    .Where(t => t.EndsWith(name, StringComparison.OrdinalIgnoreCase)).OrderBy(t => t.Length)
                    .ThenBy(t => t).ToArray();
                // Remove sub dirs
                var result = new List<string>();
                foreach (var d in dirs)
                {
                    if (result.Any(d.StartsWith))
                    {
                        continue;
                    }

                    result.Add(d);
                }

                return result.ToArray();
            }

            return new string[] { };
        }

        [HttpPost("same-name-entries-in-working-directory")]
        [SwaggerOperation(OperationId = "GetSameNameEntriesInWorkingDirectory")]
        public async Task<ListResponse<string>> GetSameNameEntriesInWorkingDirectory(
            [FromBody] RemoveSameEntryInWorkingDirectoryRequestModel model)
        {
            var paths = await _getSameNameEntriesInWorkingDirectory(model);
            return new ListResponse<string>(paths);
        }

        [HttpDelete("same-name-entry-in-working-directory")]
        [SwaggerOperation(OperationId = "RemoveSameNameEntryInWorkingDirectory")]
        public async Task<ListResponse<string>> RemoveSameNameEntryInWorkingDirectory(
            [FromBody] RemoveSameEntryInWorkingDirectoryRequestModel model)
        {
            var paths = await _getSameNameEntriesInWorkingDirectory(model);
            foreach (var be in paths)
            {
                if (System.IO.File.Exists(be))
                {
                    FileUtils.Delete(be, false, true);
                }
                else
                {
                    DirectoryUtils.Delete(be, false, true);
                }
            }

            return new ListResponse<string>(paths);
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

        private async Task _decompressFiles(BackgroundTask mainTask, FileDecompressRequestModel model)
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

                    for (var j = 0; j < allGroups.Count; j++)
                    {
                        var group = allGroups[j];
                        var entry = group.Files.FirstOrDefault();
                        if (group.Files.Count == 1 && Path.GetExtension(entry).IsNullOrEmpty())
                        {
                            const string ext = ".iw-decompressing";
                            var newFile = $"{entry}{ext}";
                            System.IO.File.Move(entry, newFile);
                            group.Files[0] = newFile;
                            entry = newFile;
                        }

                        var taskName = IwFsCompressedFile.BuildDecompressionTaskName(entry);
                        if (_backgroundTaskManager.IsRunningByName(taskName))
                        {
                            return;
                        }

                        var files = group.Files.ToArray();

                        var t = _backgroundTaskManager.RunInBackground(taskName, new CancellationTokenSource(),
                            async (task, sp) =>
                            {
                                foreach (var f in files)
                                {
                                    await _iwFsEntryTaskManager.Add(new IwFsTaskInfo(f, IwFsEntryTaskType.Decompressing,
                                        task.Id));
                                }

                                await using var scope = sp.CreateAsyncScope();

                                if (group.Password.IsNotEmpty())
                                {
                                    var passwordService = scope.ServiceProvider.GetRequiredService<PasswordService>();
                                    await passwordService.AddUsedTimes(group.Password!);
                                }

                                // for (var i = 0; i < 100; i++)
                                // {
                                //     task.Percentage = i + 1;
                                //     await Task.Delay(5_0, task.Cts.Token);
                                // }
                                //
                                // return BaseResponseBuilder.Ok;

                                var osb = new StringBuilder();
                                var esb = new StringBuilder();
                                var workingDir = Path.GetDirectoryName(entry);
                                var targetDir = Path.Combine(workingDir, group.KeyName);
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
                                        tryPSwitch ? $"-p{group.Password}" : null,
                                        tryPSwitch ? $"-aos" : null,
                                        $"-o{targetDir}",
                                        // redirect process to stderr stream
                                        "-sccUTF-8",
                                        "-scsUTF-16LE",
                                        "-bsp2"
                                    }.Where(t => t.IsNotEmpty())!, true)
                                    .WithStandardErrorPipe(PipeTarget.Merge(PipeTarget.ToDelegate(async line =>
                                    {
                                        var match = processRegex.Match(line);
                                        if (match.Success)
                                        {
                                            var np = int.Parse(match.Value.TrimEnd('%'));
                                            if (np != task.Percentage)
                                            {
                                                task.Percentage = np;
                                            }
                                        }
                                    }, Encoding.UTF8), PipeTarget.ToStringBuilder(esb, Encoding.UTF8)))
                                    .WithStandardOutputPipe(PipeTarget.ToStringBuilder(osb, Encoding.UTF8));
                                // Input password via stdin
                                if (group.Password.IsNotEmpty() && !tryPSwitch)
                                {
                                    command = command.WithStandardInputPipe(PipeSource.FromString(group.Password!, Encoding.UTF8));
                                }

                                var result = await command.ExecuteAsync();
                                if (result.ExitCode == 0)
                                {
                                    return BaseResponseBuilder.Ok;
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
                                    var badFiles = message.Split('\n', '\r').Where(a => a.StartsWith(prefix))
                                        .Select(t => Path.Combine(targetDir, t.Replace(prefix, null).Trim()))
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
                                    var directories = badFiles.Select(Path.GetDirectoryName).Distinct().SelectMany(
                                        dir =>
                                        {
                                            var chain = dir.Replace(targetDir, null)
                                                .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                                                .Where(a => a.IsNotEmpty()).ToArray();
                                            return chain.Select((t1, i) =>
                                                    Path.Combine(new[] {targetDir}.Concat(chain.Take(i + 1))
                                                        .ToArray()))
                                                .ToList();
                                        }).ToList();
                                    directories.Add(targetDir);
                                    directories = directories.OrderByDescending(t => t.Length)
                                        .Distinct().ToList();
                                    foreach (var d in directories)
                                    {
                                        var di = new DirectoryInfo(d);
                                        if (di.Exists && di.GetFileSystemInfos().Length == 0)
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
                                }

                                return BaseResponseBuilder.Build(ResponseCode.SystemError, messageSb.ToString());
                            }, BackgroundTaskLevel.Default,
                            async np => await _iwFsEntryTaskManager.Update(files, t => t.Percentage = np),
                            async () => await _iwFsEntryTaskManager.Clear(files),
                            async bt => { await _iwFsEntryTaskManager.Update(files, t => t.Error = bt.Message); }
                        );
                        await t.WaitAsync();
                        mainTask.Percentage =
                            (int) ((i + (decimal) (j + 1) / allGroups.Count) * 100 / parentsMappings.Count);
                    }
                }

                mainTask.Percentage = (i + 1) * 100 / parentsMappings.Count;
            }
        }

        [HttpPost("decompression")]
        [SwaggerOperation(OperationId = "DecompressFiles")]
        public async Task<BaseResponse> DecompressFiles([FromBody] FileDecompressRequestModel model)
        {
            var taskName = $"BatchDecompression:{DateTime.Now:HHmmss}";
            if (!_backgroundTaskManager.IsRunningByName(taskName))
            {
                _backgroundTaskManager.RunInBackground(taskName, new CancellationTokenSource(), async (task, sp) =>
                    {
                        await _decompressFiles(task, model);
                        return BaseResponseBuilder.Ok;
                    }, BackgroundTaskLevel.Default, null, null
                    // , async task => { await _iwFsEntryTaskManager.Update(model.Paths, t => t.Error = task.Message); }
                );
            }

            return BaseResponseBuilder.Ok;
        }

        private static readonly ConcurrentDictionary<string, string> IconVault = new();
        private static readonly string PngContentType = MimeTypes.GetMimeType(".png");
        private static readonly object IconVaultLock = new object();

        [HttpGet("icon")]
        [SwaggerOperation(OperationId = "GetIconData")]
        public async Task<SingletonResponse<string>> GetIcon(string path)
        {
            string? iconBase64 = null;
            if (System.IO.File.Exists(path))
            {
                var ext = Path.GetExtension(path).ToLower();
                var cacheKey = ext == InternalOptions.ExeExtension ? path : ext;
                if (!IconVault.TryGetValue(cacheKey, out iconBase64))
                {
                    lock (IconVaultLock)
                    {
                        if (!IconVault.TryGetValue(cacheKey, out iconBase64))
                        {
                            using var im = Icon.ExtractAssociatedIcon(path);
                            if (im != null)
                            {
                                using var ms = new MemoryStream();
                                // Ico encoder is not found.
                                im.ToBitmap().Save(ms, ImageFormat.Png);
                                im.Dispose();
                                ms.Seek(0, SeekOrigin.Begin);
                                iconBase64 = $@"data:{PngContentType};base64," + Convert.ToBase64String(ms.ToArray());
                            }

                            IconVault[ext] = iconBase64;
                        }
                    }
                }
            }

            return new SingletonResponse<string>(iconBase64);
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

        [HttpPut("merge-preview")]
        [SwaggerOperation(OperationId = "PreviewFileEntriesMergeResult")]
        public async Task<SingletonResponse<FileEntriesMergeResult>> MergePreview([FromBody] string[] paths)
        {
            if (paths.Length == 0)
            {
                return SingletonResponseBuilder<FileEntriesMergeResult>.NotFound;
            }

            if (paths.GroupBy(Path.GetDirectoryName).Count() > 1)
            {
                return SingletonResponseBuilder<FileEntriesMergeResult>.BuildBadRequest(
                    _localizer.PathsShouldBeInSameDirectory());
            }

            var rootPath = Path.GetDirectoryName(paths[0]).StandardizePath()!;
            var mergeResult = paths
                .GroupBy(a => Directory.Exists(a) ? Path.GetFileName(a) : Path.GetFileNameWithoutExtension(a))
                .ToDictionary(a => a.Key, a => a.Select(x => Path.GetFileName(x)!).Where(b => b != a.Key).ToArray());
            var currentNames = paths.Select(x => Path.GetFileName(x)!).ToArray();

            var result = new FileEntriesMergeResult(rootPath, currentNames, mergeResult);

            return new SingletonResponse<FileEntriesMergeResult>(result);
        }

        [HttpPut("merge-preview-in-root-path")]
        [SwaggerOperation(OperationId = "PreviewFileEntriesMergeResultInRootPath")]
        public async Task<SingletonResponse<FileEntriesMergeResult>> MergePreview([FromBody] string rootPath) =>
            await MergePreview(Directory.GetFileSystemEntries(rootPath, "*.*", SearchOption.TopDirectoryOnly));

        [HttpPut("merge")]
        [SwaggerOperation(OperationId = "MergeFileEntries")]
        public async Task<BaseResponse> Merge([FromBody] string[] paths)
        {
            var r = await MergePreview(paths);
            if (r.Code != 0)
            {
                return r;
            }

            var result = r.Data;

            foreach (var (dirName, fileNames) in result.MergeResult)
            {
                var dirFullname = Path.Combine(result.RootPath, dirName);
                Directory.CreateDirectory(dirFullname);
                foreach (var f in fileNames)
                {
                    var sourceFile = Path.Combine(result.RootPath, f);
                    var fileFullname = Path.Combine(dirFullname, f);
                    // Use quickly way in same drive
                    System.IO.File.Move(sourceFile, fileFullname, false);
                }
            }

            return BaseResponseBuilder.Ok;
        }

        [HttpPut("merge-by")]
        [SwaggerOperation(OperationId = "MergeFileEntriesInRootPath")]
        public async Task<BaseResponse> Merge([FromBody] string rootPath) =>
            await Merge(Directory.GetFileSystemEntries(rootPath, null, SearchOption.TopDirectoryOnly));

        [HttpGet("directory/file-entries")]
        [SwaggerOperation(OperationId = "GetFileSystemEntriesInDirectory")]
        public async Task<ListResponse<string>> GetFileSystemEntriesInDirectory(string path, int maxCount)
        {
            if (!Directory.Exists(path))
            {
                return new ListResponse<string>();
            }

            var di = new DirectoryInfo(path);
            using var enumerator = di.EnumerateFileSystemInfos("*.*", SearchOption.TopDirectoryOnly).GetEnumerator();
            var data = new List<string>();
            while (enumerator.MoveNext() && maxCount > data.Count)
            {
                data.Add(enumerator.Current.Name);
            }

            return new ListResponse<string>(data);
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
    }
}