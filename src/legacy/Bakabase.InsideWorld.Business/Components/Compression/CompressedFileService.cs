using CliWrap;
using Microsoft.AspNetCore.Hosting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Extensions;
using Bakabase.InsideWorld.Business.Extensions;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Extensions;
using Bootstrap.Models.Constants;
using Bootstrap.Models.ResponseModels;
using CsQuery.ExtensionMethods;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Extensions;
using Microsoft.AspNetCore.Http;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.SevenZip;
using Bakabase.InsideWorld.Business.Components.Dependency.Abstractions.Models.Constants;
using Microsoft.Extensions.Logging;

namespace Bakabase.InsideWorld.Business.Components.Compression
{
    public class CompressedFileService
    {
        private readonly IWebHostEnvironment _env;
        private readonly SevenZipService? _sevenZipService;
        private readonly ILogger<CompressedFileService> _logger;

        public CompressedFileService(IWebHostEnvironment env, ILogger<CompressedFileService> logger,
            SevenZipService? sevenZipService = null)
        {
            _env = env;
            _logger = logger;
            _sevenZipService = sevenZipService;
        }

        private string? GetSevenZipExecutable()
        {
            // Use SevenZipService if available and installed
            if (_sevenZipService?.Status == DependentComponentStatus.Installed)
            {
                try
                {
                    return _sevenZipService.SevenZipExecutable;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to get 7z executable from SevenZipService");
                }
            }

            // No 7z available
            _logger.LogWarning("7z executable not found. SevenZipService status: {Status}",
                _sevenZipService?.Status.ToString() ?? "null");
            return null;
        }

        public async Task ExtractToCurrentDirectory(string compressedFilePath, bool overwrite, CancellationToken ct)
        {
            var sevenZipExe = GetSevenZipExecutable();
            if (sevenZipExe == null)
            {
                throw new Exception("7z executable not found. Please install 7-Zip via SevenZipService.");
            }

            var password = compressedFilePath.GetPasswordsFromPath().FirstOrDefault();
            var dir = Path.GetDirectoryName(compressedFilePath);

            var arguments = new List<string>
            {
                password.IsNotEmpty() ? $"-p{password}" : null!,
                overwrite ? "-y" : null!,
                $"-o{dir}",
                "x", compressedFilePath
            }.Where(a => a.IsNotEmpty()).ToArray();

            var command = Cli.Wrap(sevenZipExe).WithArguments(arguments, true);

            var result = await command.ExecuteAsync(ct);
            if (result.ExitCode != 0)
            {
                throw new Exception($"Got exit code: {result.ExitCode} while executing [{command}]");
            }
        }

        public async Task<MemoryStream> ExtractOneEntry(string compressedFilePath, string entryPath,
            CancellationToken ct)
        {
            var sevenZipExe = GetSevenZipExecutable();
            if (sevenZipExe == null)
            {
                throw new Exception("7z executable not found. Please install 7-Zip via SevenZipService.");
            }

            var password = compressedFilePath.GetPasswordsFromPath().FirstOrDefault();

            var arguments = new List<string>
            {
                password.IsNotEmpty() ? $"-p{password}" : null!,
                "-so",
                "e", compressedFilePath,
                entryPath
            }.Where(a => a.IsNotEmpty()).ToArray();

            var ms = new MemoryStream();
            var command = Cli.Wrap(sevenZipExe).WithArguments(arguments, true)
                .WithStandardOutputPipe(PipeTarget.ToStream(ms));

            var result = await command.ExecuteAsync(ct);
            if (result.ExitCode == 0)
            {
                ms.Seek(0, SeekOrigin.Begin);
                return ms;
            }

            return null;
        }

        public async Task<ListResponse<CompressedFileEntry>> GetCompressFileEntries(string compressFilePath,
            CancellationToken ct)
        {
            if (File.Exists(compressFilePath))
            {
                var sevenZipExe = GetSevenZipExecutable();
                if (sevenZipExe == null)
                {
                    return ListResponseBuilder<CompressedFileEntry>.Build(ResponseCode.SystemError,
                        "7z executable not found. Please install 7-Zip via SevenZipService.");
                }

                var password = compressFilePath.GetPasswordsFromPath().FirstOrDefault();
                var sb = new StringBuilder();

                var arguments = new List<string>
                {
                    password.IsNotEmpty() ? $"-p{password}" : null!,
                    "l", compressFilePath
                }.Where(a => a.IsNotEmpty()).ToArray();

                var command = Cli.Wrap(sevenZipExe)
                    .WithArguments(arguments, true)
                    .WithValidation(CommandResultValidation.None)
                    .WithStandardOutputPipe(PipeTarget.ToStringBuilder(sb));
                var result = await command.ExecuteAsync(ct);
                if (result.ExitCode == 0)
                {
                    var outputStr = sb.ToString();
                    var lines = outputStr.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).ToList();
                    var tableStartLineIndex = lines.FindIndex(a => a.StartsWith("--------"));
                    var tableEndLineIndex = lines.FindLastIndex(a => a.StartsWith("--------"));
                    var fileEntries = new List<CompressedFileEntry>();
                    if (tableStartLineIndex > -1 && tableEndLineIndex > tableStartLineIndex)
                    {
                        var columnRanges = new List<Range>();
                        {
                            var tableStartLineStr = lines[tableStartLineIndex];
                            var index = -1;
                            for (var i = 0; i < tableStartLineStr.Length; i++)
                            {
                                var c = tableStartLineStr[i];
                                if (c == '-' && index == -1)
                                {
                                    index = i;
                                }

                                if ((i < tableStartLineStr.Length - 1 && tableStartLineStr[i + 1] != '-' ||
                                     i == tableStartLineStr.Length - 1) && index != -1)
                                {
                                    columnRanges.Add(new Range(index, i));
                                    index = -1;
                                }
                            }
                        }

                        var titleLineStr = lines[tableStartLineIndex - 1];
                        var columnTitles = columnRanges.Select(a =>
                            titleLineStr.Substring(a.Start.Value,
                                Math.Min(a.End.Value, titleLineStr.Length - 1) - a.Start.Value + 1).Trim()).ToArray();

                        var attrColumn = columnTitles.IndexOf("Attr");
                        var nameColumn = columnTitles.IndexOf("Name");
                        var sizeColumn = columnTitles.IndexOf("Size");

                        if (attrColumn > -1 && nameColumn > -1)
                        {
                            var dataLines = lines.Skip(tableStartLineIndex + 1)
                                .Take(tableEndLineIndex - tableStartLineIndex - 1).ToArray();
                            var attrRange = columnRanges[attrColumn];
                            var nameRange = Range.StartAt(columnRanges[nameColumn].Start);
                            foreach (var line in dataLines)
                            {
                                var attr = line.Substring(attrRange.Start.Value,
                                    attrRange.End.Value - attrRange.Start.Value + 1).Trim();
                                if (attr.EndsWith('A'))
                                {
                                    var path = line[nameRange.Start.Value..].Trim();
                                    var fe = new CompressedFileEntry
                                    {
                                        Path = path.StandardizePath()!
                                    };
                                    if (sizeColumn > -1)
                                    {
                                        var sizeRange = columnRanges[sizeColumn];
                                        var sizeStr = line.Substring(sizeRange.Start.Value,
                                            sizeRange.End.Value - sizeRange.Start.Value + 1).Trim();
                                        if (long.TryParse(sizeStr, out var size))
                                        {
                                            fe.Size = size;
                                        }
                                    }

                                    fileEntries.Add(fe);
                                }
                            }
                        }
                    }

                    return new ListResponse<CompressedFileEntry>(fileEntries);
                }

                return ListResponseBuilder<CompressedFileEntry>.Build(ResponseCode.SystemError,
                    $"Failed to execute [{command}], exit code is {result.ExitCode}");
            }

            return ListResponseBuilder<CompressedFileEntry>.NotFound;
        }

        public class TestResult
        {
            public int ExitCode { get; set; }
            public string StandardOutput { get; set; } = string.Empty;
            public string StandardError { get; set; } = string.Empty;
        }

        /// <summary>
        /// Test a compressed file with optional password
        /// </summary>
        /// <param name="compressedFilePath">Path to the compressed file</param>
        /// <param name="password">Optional password</param>
        /// <param name="workingDirectory">Working directory for the command</param>
        /// <param name="onStandardOutput">Callback for standard output lines</param>
        /// <param name="onStandardError">Callback for standard error lines</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>Test result with exit code and output</returns>
        public async Task<TestResult> TestCompressedFile(
            string compressedFilePath,
            string? password,
            string? workingDirectory,
            Action<string>? onStandardOutput,
            Action<string>? onStandardError,
            CancellationToken ct)
        {
            var sevenZipExe = GetSevenZipExecutable();
            if (sevenZipExe == null)
            {
                throw new Exception("7z executable not found. Please install 7-Zip via SevenZipService.");
            }

            var osb = new StringBuilder();
            var esb = new StringBuilder();

            var args = new List<string?>
            {
                "t",
                compressedFilePath,
                password.IsNotEmpty() ? $"-p{password}" : null,
                "-sccUTF-8",
                "-scsUTF-16LE",
                "-bsp2",
                "-bb1"
            }.OfType<string>().ToArray();

            var command = Cli.Wrap(sevenZipExe)
                .WithValidation(CommandResultValidation.None)
                .WithArguments(args, true);

            if (workingDirectory.IsNotEmpty())
            {
                command = command.WithWorkingDirectory(workingDirectory!);
            }

            var stdoutTargets = new List<PipeTarget> { PipeTarget.ToStringBuilder(osb, Encoding.UTF8) };
            if (onStandardOutput != null)
            {
                stdoutTargets.Add(PipeTarget.ToDelegate(onStandardOutput, Encoding.UTF8));
            }

            var stderrTargets = new List<PipeTarget> { PipeTarget.ToStringBuilder(esb, Encoding.UTF8) };
            if (onStandardError != null)
            {
                stderrTargets.Add(PipeTarget.ToDelegate(onStandardError, Encoding.UTF8));
            }

            command = command
                .WithStandardOutputPipe(PipeTarget.Merge(stdoutTargets.ToArray()))
                .WithStandardErrorPipe(PipeTarget.Merge(stderrTargets.ToArray()));

            var result = await command.ExecuteAsync(ct);

            return new TestResult
            {
                ExitCode = result.ExitCode,
                StandardOutput = osb.ToString(),
                StandardError = esb.ToString()
            };
        }

        public enum OverwriteMode
        {
            /// <summary>No overwrite handling specified</summary>
            None,
            /// <summary>Overwrite all existing files without prompt (-aoa)</summary>
            OverwriteAll,
            /// <summary>Skip extracting of existing files (-aos)</summary>
            SkipExisting
        }

        public enum ProgressOutputTarget
        {
            /// <summary>Progress to stdout (-bsp1)</summary>
            StandardOutput,
            /// <summary>Progress to stderr (-bsp2)</summary>
            StandardError
        }

        /// <summary>
        /// Extract compressed file with progress reporting
        /// </summary>
        /// <param name="compressedFilePath">Path to the compressed file (first file for multi-part archives)</param>
        /// <param name="targetDirectory">Target directory for extraction</param>
        /// <param name="password">Optional password (if null, will prompt via stdin)</param>
        /// <param name="usePasswordSwitch">If true, use -p switch; if false, use stdin for password</param>
        /// <param name="overwriteMode">Overwrite mode for existing files</param>
        /// <param name="progressOutput">Where to output progress information</param>
        /// <param name="workingDirectory">Working directory for the command</param>
        /// <param name="onStandardOutput">Callback for standard output lines</param>
        /// <param name="onStandardError">Callback for standard error lines</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>Test result with exit code and output</returns>
        public async Task<TestResult> ExtractWithProgress(
            string compressedFilePath,
            string targetDirectory,
            string? password,
            bool usePasswordSwitch,
            OverwriteMode overwriteMode,
            ProgressOutputTarget progressOutput,
            string? workingDirectory,
            Action<string>? onStandardOutput,
            Action<string>? onStandardError,
            CancellationToken ct)
        {
            var sevenZipExe = GetSevenZipExecutable();
            if (sevenZipExe == null)
            {
                throw new Exception("7z executable not found. Please install 7-Zip via SevenZipService.");
            }

            var osb = new StringBuilder();
            var esb = new StringBuilder();

            var overwriteArg = overwriteMode switch
            {
                OverwriteMode.OverwriteAll => "-aoa",
                OverwriteMode.SkipExisting => "-aos",
                _ => null
            };

            var progressArg = progressOutput switch
            {
                ProgressOutputTarget.StandardError => "-bsp2",
                _ => "-bsp1"
            };

            var args = new List<string?>
            {
                "x",
                compressedFilePath,
                (password.IsNotEmpty() && usePasswordSwitch) ? $"-p{password}" : null,
                $"-o{targetDirectory}",
                overwriteArg,
                "-sccUTF-8",
                "-scsUTF-16LE",
                progressArg
            }.OfType<string>().ToArray();

            var command = Cli.Wrap(sevenZipExe)
                .WithValidation(CommandResultValidation.None)
                .WithArguments(args, true);

            if (workingDirectory.IsNotEmpty())
            {
                command = command.WithWorkingDirectory(workingDirectory!);
            }

            // Input password via stdin if not using -p switch
            if (password.IsNotEmpty() && !usePasswordSwitch)
            {
                command = command.WithStandardInputPipe(PipeSource.FromString(password!, Encoding.UTF8));
            }

            var stdoutTargets = new List<PipeTarget> { PipeTarget.ToStringBuilder(osb, Encoding.UTF8) };
            if (onStandardOutput != null)
            {
                stdoutTargets.Add(PipeTarget.ToDelegate(onStandardOutput, Encoding.UTF8));
            }

            var stderrTargets = new List<PipeTarget> { PipeTarget.ToStringBuilder(esb, Encoding.UTF8) };
            if (onStandardError != null)
            {
                stderrTargets.Add(PipeTarget.ToDelegate(onStandardError, Encoding.UTF8));
            }

            command = command
                .WithStandardOutputPipe(PipeTarget.Merge(stdoutTargets.ToArray()))
                .WithStandardErrorPipe(PipeTarget.Merge(stderrTargets.ToArray()));

            var result = await command.ExecuteAsync(ct);

            return new TestResult
            {
                ExitCode = result.ExitCode,
                StandardOutput = osb.ToString(),
                StandardError = esb.ToString()
            };
        }
    }
}