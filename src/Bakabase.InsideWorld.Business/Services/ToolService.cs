using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.ResponseModels;
using Bootstrap.Components.Logging.LogService.Services;
using Bootstrap.Components.Storage;
using Bootstrap.Extensions;
using Bootstrap.Models.ResponseModels;
using CliWrap;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Org.BouncyCastle.Tls;
using SharpCompress.Archives;
using SharpCompress.Archives.Rar;
using SharpCompress.Archives.SevenZip;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace Bakabase.InsideWorld.Business.Services
{
    public class ToolService
    {
        private CancellationTokenSource _everythingExtractionCts;
        private readonly LogService _logService;
        private readonly ILogger<ToolService> _logger;
        private readonly IHostEnvironment _env;

        public ToolService(LogService logService, ILogger<ToolService> logger, IHostEnvironment env)
        {
            _logService = logService;
            _logger = logger;
            _env = env;
        }

        public EverythingExtractionStatus EverythingExtractionStatus { private set; get; } = new();

        /// <summary>
        /// https://github.com/Bakabase/InsideWorld/issues/66
        /// </summary>
        /// <param name="root"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public Task StartExtractEverything(string root)
        {
            if (_everythingExtractionCts?.IsCancellationRequested == false)
            {
                throw new Exception("Previous job is running");
            }


            var localStatus = EverythingExtractionStatus = new EverythingExtractionStatus()
                {Running = true, StartDt = DateTime.Now};
            _everythingExtractionCts = new CancellationTokenSource();
            var ct = _everythingExtractionCts.Token;
            ct.Register(() => localStatus.Running = false);
            Task.Run(async () =>
            {
                try
                {
                    if (Directory.Exists(root))
                    {
                        var directories = new[] {root}.Concat(Directory.GetDirectories(root, "*.*")).ToArray();

                        var compressedFileExtensionRegexPart =
                            string.Join('|',
                                InternalOptions.CompressedFileExtensions.Select(a => Regex.Escape(a.TrimStart('.'))));
                        // 文件名.z01
                        // 文件名.zip
                        // 文件名.zip.001
                        // 文件名.zip.part1
                        // 文件名.part1.zip
                        var reg = new Regex(
                            $@"^(?<name>.*?)(\[(?<password>[^\[\]]*?)\])?(\.(?<ext1>{compressedFileExtensionRegexPart}))?(\.(?<part>[a-zA-Z]*\d+))?(\.(?<ext2>{compressedFileExtensionRegexPart}))?$",
                            RegexOptions.IgnoreCase);

                        // directory - title - list
                        var allCompressedFileGroups =
                            new Dictionary<string,
                                Dictionary<string, List<(string Fullname, string Extension, string Password)>>>();
                        foreach (var directory in directories)
                        {
                            var files = Directory.GetFiles(directory);
                            if (!allCompressedFileGroups.TryGetValue(directory, out var directoryCompressedFileGroups))
                            {
                                directoryCompressedFileGroups = allCompressedFileGroups[directory] = new();
                            }

                            foreach (var fullname in files)
                            {
                                var match = reg.Match(Path.GetFileName(fullname));
                                if (match.Groups["ext1"].Success || match.Groups["ext2"].Success ||
                                    match.Groups["part"].Success)
                                {
                                    var name = match.Groups["name"].Value;
                                    if (!directoryCompressedFileGroups.TryGetValue(name, out var list))
                                    {
                                        list = directoryCompressedFileGroups[name] =
                                            new List<(string Fullname, string Extension, string Password)>();
                                    }

                                    var ext = match.Groups["ext2"].Success
                                        ? match.Groups["ext2"].Value
                                        : match.Groups["ext1"]
                                            .Value;
                                    if (ext.IsNotEmpty())
                                    {
                                        ext = $".{ext}";
                                    }

                                    list.Add((fullname, ext, match.Groups["password"].Value));
                                }
                            }

                            foreach (var (name, list) in directoryCompressedFileGroups)
                            {
                                var password = list.FirstOrDefault(a => a.Password.IsNotEmpty()).Password;
                                if (password.IsNotEmpty())
                                {
                                    var newList = new List<(string Fullname, string Extension, string Password)>();
                                    var targetName = $"{name}[{password}]";
                                    // 确保所有分P文件都包含密码（需要同名）
                                    foreach (var item in list)
                                    {
                                        var (fullname, ext, pw) = item;
                                        if (pw.IsNotEmpty())
                                        {
                                            newList.Add(item);
                                        }
                                        else
                                        {
                                            var targetFullname = Path.Combine(directory,
                                                Path.GetFileName(fullname).Replace(name, targetName));
                                            File.Move(fullname, targetFullname);
                                            newList.Add((targetFullname, ext, password));
                                        }
                                    }

                                    directoryCompressedFileGroups[name] = newList.OrderBy(a => a.Fullname).ToList();
                                }
                            }
                        }

                        EverythingExtractionStatus.TotalCount = allCompressedFileGroups.Sum(a => a.Value.Count);

                        foreach (var (directory, directoryCompressedFileGroups) in allCompressedFileGroups)
                        {
                            foreach (var (name, list) in directoryCompressedFileGroups)
                            {
                                var (fullname, _, password) = list.FirstOrDefault();
                                EverythingExtractionStatus.Current = Path.Combine(directory, name);
                                var targetDirectory = Path.Combine(directory, name);
                                try
                                {
                                    await UnpackFile(fullname, targetDirectory, password, ct);
                                    // var is7Z = orderedList.Any(a =>
                                    //     BusinessConstants.SevenZipCompressedFileExtension.Equals(a.Extension,
                                    //         StringComparison.OrdinalIgnoreCase));
                                    //
                                    // var ms = new MemoryStream();
                                    // foreach (var (fullname, _, _) in orderedList)
                                    // {
                                    //     await using var s = File.OpenRead(fullname);
                                    //     await s.CopyToAsync(ms, ct);
                                    // }
                                    //
                                    // var password = orderedList.FirstOrDefault(a => a.Password.IsNotEmpty()).Password;
                                    //
                                    // ms.Seek(0, SeekOrigin.Begin);
                                    // Directory.CreateDirectory(targetDirectory);
                                    // IReader reader;
                                    // var readerOptions = new ReaderOptions {Password = password};
                                    // // 7z
                                    // if (is7Z)
                                    // {
                                    //     using var archive = SevenZipArchive.Open(ms, readerOptions);
                                    //     reader = archive.ExtractAllEntries();
                                    // }
                                    // // others
                                    // else
                                    // {
                                    //     reader = ReaderFactory.Open(ms, readerOptions);
                                    // }
                                    //
                                    // reader.WriteAllToDirectory(targetDirectory);
                                    // reader.Dispose();

                                    EverythingExtractionStatus.DoneCount++;
                                }
                                catch (Exception e)
                                {
                                    DirectoryUtils.Delete(targetDirectory, true, false);
                                    ct.ThrowIfCancellationRequested();

                                    var infoSegments = new[]
                                    {
                                        $"An error occurred during extracting", Path.Combine(directory, name),
                                        "with files:",
                                    }.Concat(list.Select(a => Path.GetFileName(a.Fullname))).ToList();
                                    infoSegments.Add(e.BuildFullInformationText());
                                    await _logService.Log($"ExtractEverything", LogLevel.Error, "Extracting",
                                        string.Join(Environment.NewLine, infoSegments));
                                    EverythingExtractionStatus.FailedCount += 1;
                                    EverythingExtractionStatus.Failures.Insert(0, new EverythingExtractionStatus.Failure
                                    {
                                        FullnameList = list.Select(a => Path.Combine(directory, a.Fullname))
                                            .ToArray(),
                                        Error = e.Message
                                    });
                                }
                                finally
                                {

                                }
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    _logger.LogError(e, e.Message);
                }
                finally
                {
                    _everythingExtractionCts?.Cancel();
                }
            }, ct);
            return Task.CompletedTask;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="fullname"></param>
        /// <param name="outputDirectory"></param>
        /// <param name="password"></param>
        /// <param name="ct"></param>
        /// <returns>Output from cli.</returns>
        private async Task UnpackFile(string fullname, string outputDirectory, string password, CancellationToken ct)
        {
            var sevenZExecutable = Path.Combine(_env.ContentRootPath, "libs/7z.exe");
            var arguments = new List<string>
            {
                "e",
                fullname,
                "-aos",
                $@"-o{outputDirectory}"
            };
            if (password.IsNotEmpty())
            {
                arguments.Add($"-p{password}");
            }

            var cleanArguments = arguments.Select(a => a.Trim()).ToArray();

            var sb = new StringBuilder();
            var cmd = Cli.Wrap(sevenZExecutable)
                .WithValidation(CommandResultValidation.None)
                .WithArguments(cleanArguments, true)
                .WithStandardOutputPipe(PipeTarget.ToStringBuilder(sb));
            var result = await cmd.ExecuteAsync(ct);
            if (result.ExitCode != 0)
            {
                throw new Exception($"Exit code: {result.ExitCode}, {sb}");
            }
        }

        public Task StopExtractEverything()
        {
            _everythingExtractionCts?.Cancel();
            return Task.CompletedTask;
        }

        public static async Task<MemoryStream> ExtractFile(string compressedFile, string key)
        {
            var file = new FileInfo(compressedFile);
            if (!file.Exists)
            {
                return null;
            }

            MemoryStream keyStream = null;

            if (InternalOptions.SevenZipCompressedFileExtension.Equals(file.Extension,
                    StringComparison.OrdinalIgnoreCase))
            {
                var archive = SevenZipArchive.Open(file.FullName);
                var keyFile =
                    archive.Entries.FirstOrDefault(a => a.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
                if (keyFile != null)
                {
                    await using var s = keyFile.OpenEntryStream();
                    await s.CopyToAsync(keyStream = new MemoryStream());
                    keyStream.Seek(0, SeekOrigin.Begin);
                }
            }
            else
            {
                await using Stream stream = file.OpenRead();
                using var reader = ReaderFactory.Open(stream);
                while (reader.MoveToNextEntry())
                {
                    if (reader.Entry.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
                    {
                        await using var s = reader.OpenEntryStream();
                        await s.CopyToAsync(keyStream = new MemoryStream());
                        keyStream.Seek(0, SeekOrigin.Begin);
                        break;
                    }
                }
            }

            return keyStream;
        }
    }
}