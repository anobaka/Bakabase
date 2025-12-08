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

        public async Task<MemoryStream?> ExtractOneEntry(string compressedFilePath, string entryPath,
            CancellationToken ct)
        {
            var sevenZipExe = GetSevenZipExecutable();
            if (sevenZipExe == null)
            {
                throw new Exception("7z executable not found. Please install 7-Zip via SevenZipService.");
            }

            var password = compressedFilePath.GetPasswordsFromPath().FirstOrDefault();

            // Use 'e' command with -so to extract to stdout
            // -y to assume yes on all queries
            // The entry path should match exactly what's in the archive (as returned by GetCompressFileEntries)
            var arguments = new List<string>
            {
                "e",                    // extract command
                "-so",                  // write to stdout
                "-y",                   // assume yes
                password.IsNotEmpty() ? $"-p{password}" : null!,
                compressedFilePath,
                entryPath               // specific file to extract (use exact path from archive)
            }.Where(a => a.IsNotEmpty()).ToArray();

            var ms = new MemoryStream();
            var stderrBuilder = new StringBuilder();
            var command = Cli.Wrap(sevenZipExe)
                .WithArguments(arguments, true)
                .WithValidation(CommandResultValidation.None)
                .WithStandardOutputPipe(PipeTarget.ToStream(ms))
                .WithStandardErrorPipe(PipeTarget.ToStringBuilder(stderrBuilder));

            _logger.LogDebug("Extracting entry '{EntryPath}' from '{CompressedFilePath}' with command: {Command}",
                entryPath, compressedFilePath, command);

            var result = await command.ExecuteAsync(ct);
            if (result.ExitCode == 0 && ms.Length > 0)
            {
                ms.Seek(0, SeekOrigin.Begin);
                return ms;
            }

            // Log error for debugging
            _logger.LogWarning(
                "Failed to extract entry '{EntryPath}' from '{CompressedFilePath}'. Exit code: {ExitCode}, StdErr: {Stderr}, StreamLength: {Length}",
                entryPath, compressedFilePath, result.ExitCode, stderrBuilder.ToString(), ms.Length);

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

                // Use -slt for machine-readable "technical listing" format (consistent across 7z versions)
                var arguments = new List<string>
                {
                    password.IsNotEmpty() ? $"-p{password}" : null!,
                    "l",
                    "-slt", // Technical listing format with key=value pairs
                    compressFilePath
                }.Where(a => a.IsNotEmpty()).ToArray();

                var command = Cli.Wrap(sevenZipExe)
                    .WithArguments(arguments, true)
                    .WithValidation(CommandResultValidation.None)
                    .WithStandardOutputPipe(PipeTarget.ToStringBuilder(sb));
                var result = await command.ExecuteAsync(ct);
                if (result.ExitCode == 0)
                {
                    var outputStr = sb.ToString();
                    var fileEntries = ParseTechnicalListingOutput(outputStr);
                    return new ListResponse<CompressedFileEntry>(fileEntries);
                }

                return ListResponseBuilder<CompressedFileEntry>.Build(ResponseCode.SystemError,
                    $"Failed to execute [{command}], exit code is {result.ExitCode}");
            }

            return ListResponseBuilder<CompressedFileEntry>.NotFound;
        }

        /// <summary>
        /// Parses the technical listing output from 7z l -slt command.
        /// The format is consistent across 7z versions and uses key = value pairs.
        /// Each file entry is separated by blank lines.
        /// </summary>
        private static List<CompressedFileEntry> ParseTechnicalListingOutput(string output)
        {
            var fileEntries = new List<CompressedFileEntry>();
            var lines = output.Split('\n');

            string? currentPath = null;
            long currentSize = 0;
            string? currentAttributes = null;

            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();

                if (string.IsNullOrEmpty(line))
                {
                    // End of current entry block - save if it's a file (not a directory)
                    if (currentPath != null && currentAttributes != null)
                    {
                        // Check if it's a file (not a directory)
                        // In 7z technical listing, directories have 'D' in attributes, files have 'A'
                        if (!currentAttributes.Contains('D'))
                        {
                            fileEntries.Add(new CompressedFileEntry
                            {
                                // Keep the original path from the archive - don't standardize
                                // This ensures the path matches exactly what 7z expects for extraction
                                Path = currentPath,
                                Size = currentSize
                            });
                        }
                    }

                    // Reset for next entry
                    currentPath = null;
                    currentSize = 0;
                    currentAttributes = null;
                    continue;
                }

                // Parse key = value pairs
                var separatorIndex = line.IndexOf(" = ", StringComparison.Ordinal);
                if (separatorIndex > 0)
                {
                    var key = line[..separatorIndex].Trim();
                    var value = line[(separatorIndex + 3)..].Trim();

                    switch (key)
                    {
                        case "Path":
                            currentPath = value;
                            break;
                        case "Size":
                            if (long.TryParse(value, out var size))
                            {
                                currentSize = size;
                            }
                            break;
                        case "Attributes":
                            currentAttributes = value;
                            break;
                    }
                }
            }

            // Handle last entry if output doesn't end with blank line
            if (currentPath != null && currentAttributes != null && !currentAttributes.Contains('D'))
            {
                fileEntries.Add(new CompressedFileEntry
                {
                    // Keep the original path from the archive
                    Path = currentPath,
                    Size = currentSize
                });
            }

            return fileEntries;
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