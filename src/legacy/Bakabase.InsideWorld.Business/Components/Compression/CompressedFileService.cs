using CliWrap;
using Microsoft.AspNetCore.Hosting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Extensions;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Extensions;
using Bootstrap.Models.Constants;
using Bootstrap.Models.ResponseModels;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.SevenZip;
using Bakabase.InsideWorld.Business.Components.Dependency.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Dependency.Abstractions;
using Bakabase.InsideWorld.Business.Components.Dependency.Exceptions;
using Microsoft.Extensions.Logging;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.CloudTools;

namespace Bakabase.InsideWorld.Business.Components.Compression
{
    public partial class CompressedFileService
    {
        private readonly IWebHostEnvironment _env;
        private readonly SevenZipService? _sevenZipService;
        private readonly ILogger<CompressedFileService> _logger;
        private readonly IDependencyLocalizer? _dependencyLocalizer;
        private readonly PyzipperService? _pyzipperService;

        // Compound formats that require two-stage extraction (e.g., .tar.gz -> .tar -> files)
        private static readonly HashSet<string> CompoundFormats = new(StringComparer.OrdinalIgnoreCase)
        {
            ".tar.gz", ".tar.bz2", ".tar.xz", ".tar.Z", ".tar.lz", ".tar.lzma", ".tgz"
        };

        public CompressedFileService(IWebHostEnvironment env, ILogger<CompressedFileService> logger,
            SevenZipService? sevenZipService = null, IDependencyLocalizer? dependencyLocalizer = null,
            PyzipperService? pyzipperService = null)
        {
            _env = env;
            _logger = logger;
            _sevenZipService = sevenZipService;
            _dependencyLocalizer = dependencyLocalizer;
            _pyzipperService = pyzipperService;
        }

        protected virtual Task<PythonRuntimeExecution> GetZipCompatibilityExecutionAsync(CancellationToken ct) =>
            _pyzipperService?.CaptureReadyExecutionAsync(ct) ?? throw new DependencyNotInstalledException(
                "Pyzipper", "pyzipper", "Install pyzipper in dependency management to use ZIP compatibility reading.", PyzipperService.ComponentId);

        private async Task<ZipPasswordCompatibility.Result> RunZipCompatibilityAsync(string operation, string path,
            string? destination, string? password, ArchiveOpenOptions options, Action<string>? progress, CancellationToken ct) =>
            await ZipPasswordCompatibility.RunAsync(await GetZipCompatibilityExecutionAsync(ct), operation,
                path, destination, password, options, progress, ct);

        protected virtual string? GetSevenZipExecutable()
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

            // Log warning with current status
            _logger.LogWarning("7z executable not found. SevenZipService status: {Status}",
                _sevenZipService?.Status.ToString() ?? "null");
            return null;
        }

        private async Task<string?> GetSevenZipExecutableAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (_sevenZipService != null)
            {
                try
                {
                    await _sevenZipService.EnsureReadyAsync(ct);
                }
                catch (DependencyNotInstalledException)
                {
                    return null;
                }
            }

            return GetSevenZipExecutable();
        }

        private Exception CreateDependencyNotInstalledException()
        {
            var sevenZipDisplayName = _sevenZipService?.DisplayName ?? "7-Zip";

            // Check if 7z is currently being installed
            if (_sevenZipService?.Status == DependentComponentStatus.Installing)
            {
                var message = _dependencyLocalizer?.Dependency_Installing_Message(sevenZipDisplayName)
                    ?? $"{sevenZipDisplayName} is currently being installed. Please wait for it to complete.";
                return new DependencyNotInstalledException("7z", sevenZipDisplayName, message, _sevenZipService?.Id);
            }

            // 7z is not installed
            var notInstalledMessage = _dependencyLocalizer?.Dependency_NotInstalled_Message(sevenZipDisplayName)
                ?? $"{sevenZipDisplayName} is required for archive extraction. Please install it in the system settings page.";
            return new DependencyNotInstalledException("7z", sevenZipDisplayName, notInstalledMessage, _sevenZipService?.Id);
        }

        public async Task ExtractToCurrentDirectory(string compressedFilePath, bool overwrite, CancellationToken ct)
        {
            var sevenZipExe = await GetSevenZipExecutableAsync(ct);
            if (sevenZipExe == null)
            {
                throw CreateDependencyNotInstalledException();
            }

            var fileName = Path.GetFileName(compressedFilePath);

            // Check if this is a compound format requiring two-stage extraction
            var compoundExt = CompoundFormats
                .FirstOrDefault(ext => fileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase));

            if (compoundExt != null)
            {
                await ExtractCompoundFormat(compressedFilePath, compoundExt, overwrite, ct);
            }
            else
            {
                await ExtractSingleStage(compressedFilePath, Path.GetDirectoryName(compressedFilePath)!, overwrite, ct);
            }
        }

        private async Task ExtractSingleStage(string compressedFilePath, string outputDir, bool overwrite, CancellationToken ct)
        {
            var password = compressedFilePath.GetPasswordsFromPath().FirstOrDefault();
            var result = await ExtractWithProgress(compressedFilePath, outputDir, password, true,
                overwrite ? OverwriteMode.OverwriteAll : OverwriteMode.None, ProgressOutputTarget.StandardOutput,
                null, null, null, ct);
            if (result.ExitCode != 0)
            {
                throw new InvalidDataException($"Archive extraction failed (exit code {result.ExitCode}): {result.StandardError}");
            }
        }

        private async Task ExtractCompoundFormat(string compressedFilePath, string compoundExt, bool overwrite, CancellationToken ct)
        {
            var targetDir = Path.GetDirectoryName(compressedFilePath)!;
            var tempDir = Path.Combine(Path.GetTempPath(), $"bakabase_extract_{Guid.NewGuid():N}");

            try
            {
                Directory.CreateDirectory(tempDir);

                // Stage 1: Extract outer layer to temp directory (e.g., .tar.gz -> .tar)
                await ExtractSingleStage(compressedFilePath, tempDir, true, ct);

                // Find the intermediate file (should be the only file in temp dir)
                var intermediateFiles = Directory.GetFiles(tempDir);
                if (intermediateFiles.Length == 0)
                {
                    _logger.LogWarning("No intermediate file found after first extraction of '{CompressedFile}'", compressedFilePath);
                    return;
                }

                // Stage 2: Extract inner layer to target directory
                foreach (var intermediateFile in intermediateFiles)
                {
                    await ExtractSingleStage(intermediateFile, targetDir, overwrite, ct);
                }
            }
            finally
            {
                // Clean up temp directory
                try
                {
                    if (Directory.Exists(tempDir))
                    {
                        Directory.Delete(tempDir, true);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to clean up temp directory: {TempDir}", tempDir);
                }
            }
        }

        public async Task<MemoryStream?> ExtractOneEntry(string compressedFilePath, string entryPath,
            CancellationToken ct)
        {
            var opening = await ResolveAutomaticOpeningAsync(compressedFilePath, null, ct);
            if (opening?.UsesZipCompatibilityReader == true)
                throw new NotSupportedException("This archive requires compatibility extraction before previewing a member.");
            var sevenZipExe = await GetSevenZipExecutableAsync(ct);
            if (sevenZipExe == null)
            {
                throw CreateDependencyNotInstalledException();
            }

            var password = compressedFilePath.GetPasswordsFromPath().FirstOrDefault();

            // Use 'e' command with -so to extract to stdout
            // -y to assume yes on all queries
            // The entry path should match exactly what's in the archive (as returned by GetCompressFileEntries)
            var arguments = new List<string>
            {
                "e",                    // extract command
                opening?.TypeArgument!,
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
            CancellationToken ct, ArchiveOpenOptions? openOptions = null)
        {
            if (File.Exists(compressFilePath))
            {
                openOptions = await ResolveAutomaticOpeningAsync(compressFilePath, openOptions, ct);
                if (openOptions?.UsesZipCompatibilityReader == true)
                {
                    var compatibilityResult = await RunZipCompatibilityAsync("list", compressFilePath, null, null, openOptions, null, ct);
                    ZipPasswordCompatibility.RequireSuccess(compatibilityResult);
                    return new ListResponse<CompressedFileEntry>(compatibilityResult.Entries ?? []);
                }
                if (openOptions?.Mode != null)
                {
                    var catalog = await ReadParserCatalogAsync(compressFilePath, ct);
                    return new ListResponse<CompressedFileEntry>(catalog.Selected(openOptions.MemberSelector)
                        .Select(m => new CompressedFileEntry {Path = m.Name, Size = m.Size}).ToList());
                }
                if (openOptions?.ZipFileNameCodePage != null)
                {
                    ct.ThrowIfCancellationRequested();
                    using var zip = new ExplicitZipNameReader(compressFilePath, openOptions);
                    return new ListResponse<CompressedFileEntry>(zip.Entries());
                }
                var sevenZipExe = await GetSevenZipExecutableAsync(ct);
                if (sevenZipExe == null)
                {
                    var ex = CreateDependencyNotInstalledException();
                    return ListResponseBuilder<CompressedFileEntry>.Build(ResponseCode.SystemError, ex.Message);
                }

                var password = compressFilePath.GetPasswordsFromPath().FirstOrDefault();
                var sb = new StringBuilder();

                // Use -slt for machine-readable "technical listing" format (consistent across 7z versions)
                var arguments = new List<string>
                {
                    password.IsNotEmpty() ? $"-p{password}" : null!,
                    "l",
                    "-slt", // Technical listing format with key=value pairs
                    openOptions?.TypeArgument!,
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
            bool? folder = null;
            void FinishEntry()
            {
                // RAR5 can report an empty Attributes field for a regular member.
                // Prefer its explicit Folder flag; some other handlers expose only Attributes.
                if (currentPath != null && (folder == false || (folder == null &&
                        currentAttributes != null && !currentAttributes.Contains('D'))))
                    fileEntries.Add(new CompressedFileEntry {Path = currentPath, Size = currentSize});
                currentPath = null;
                currentSize = 0;
                currentAttributes = null;
                folder = null;
            }
            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();
                if (line.Length == 0)
                {
                    FinishEntry();
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
                        case "Folder":
                            folder = value switch {"+" => true, "-" => false, _ => null};
                            break;
                        case "Attributes":
                            currentAttributes = value;
                            break;
                    }
                }
            }

            FinishEntry();
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
            CancellationToken ct, ArchiveOpenOptions? openOptions = null)
        {
            openOptions = await ResolveAutomaticOpeningAsync(compressedFilePath, openOptions, ct);
            if (openOptions?.UsesZipCompatibilityReader == true)
            {
                var compatibilityResult = await RunZipCompatibilityAsync("test", compressedFilePath, null, password, openOptions, onStandardOutput, ct);
                if (compatibilityResult.Code == "ArchivePasswordRejected")
                {
                    onStandardError?.Invoke("Wrong password");
                    return new TestResult {ExitCode = 2, StandardError = "Wrong password"};
                }
                ZipPasswordCompatibility.RequireSuccess(compatibilityResult);
                return new TestResult {ExitCode = 0};
            }
            if (openOptions?.Mode != null)
                return await ProcessParserAsync(compressedFilePath, null, openOptions, onStandardOutput, ct);
            if (openOptions?.ZipFileNameCodePage != null)
            {
                ct.ThrowIfCancellationRequested();
                using var zip = new ExplicitZipNameReader(compressedFilePath, openOptions);
                await zip.ReadAsync(null, onStandardOutput, ct);
                return new TestResult {ExitCode = 0};
            }
            await EnsureAutomaticZipNamesAsync(compressedFilePath, openOptions, ct);
            var sevenZipExe = await GetSevenZipExecutableAsync(ct);
            if (sevenZipExe == null)
            {
                throw CreateDependencyNotInstalledException();
            }

            var osb = new StringBuilder();
            var esb = new StringBuilder();

            var args = new List<string?>
            {
                "t",
                openOptions?.TypeArgument,
                compressedFilePath,
                password.IsNotEmpty() ? $"-p{password}" : null,
                "-sccUTF-8",
                "-scsUTF-16LE",
                "-bsp0", // Keep terminal progress control bytes out of password/error diagnostics.
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
            CancellationToken ct, ArchiveOpenOptions? openOptions = null)
        {
            openOptions = await ResolveAutomaticOpeningAsync(compressedFilePath, openOptions, ct);
            if (openOptions?.UsesZipCompatibilityReader == true)
            {
                var compatibilityResult = await RunZipCompatibilityAsync("extract", compressedFilePath, targetDirectory, password, openOptions,
                    progressOutput == ProgressOutputTarget.StandardError ? onStandardError : onStandardOutput, ct);
                ZipPasswordCompatibility.RequireSuccess(compatibilityResult);
                return new TestResult {ExitCode = 0};
            }
            if (openOptions?.Mode != null)
                return await ProcessParserAsync(compressedFilePath, targetDirectory, openOptions,
                    progressOutput == ProgressOutputTarget.StandardError ? onStandardError : onStandardOutput, ct);
            if (openOptions?.ZipFileNameCodePage != null)
            {
                ct.ThrowIfCancellationRequested();
                using var zip = new ExplicitZipNameReader(compressedFilePath, openOptions);
                await zip.ReadAsync(targetDirectory,
                    progressOutput == ProgressOutputTarget.StandardError ? onStandardError : onStandardOutput, ct);
                return new TestResult {ExitCode = 0};
            }
            await EnsureAutomaticZipNamesAsync(compressedFilePath, openOptions, ct);
            var sevenZipExe = await GetSevenZipExecutableAsync(ct);
            if (sevenZipExe == null)
            {
                throw CreateDependencyNotInstalledException();
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
                openOptions?.TypeArgument,
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
                stdoutTargets.Add(SevenZipProgressOutput.CreatePipe(onStandardOutput));
            }

            var stderrTargets = new List<PipeTarget> { PipeTarget.ToStringBuilder(esb, Encoding.UTF8) };
            if (onStandardError != null)
            {
                stderrTargets.Add(SevenZipProgressOutput.CreatePipe(onStandardError));
            }

            command = command
                .WithStandardOutputPipe(PipeTarget.Merge(stdoutTargets.ToArray()))
                .WithStandardErrorPipe(PipeTarget.Merge(stderrTargets.ToArray()));

            var result = await command.ExecuteAsync(ct);
            var diagnostic = new TestResult
            {
                ExitCode = result.ExitCode,
                StandardOutput = osb.ToString(),
                StandardError = esb.ToString()
            };
            if (HasZipNameDecodingFailure(diagnostic))
                throw new ArchiveNameEncodingUnsupportedException(
                    "The ZIP tool could not create files with the decoded names. Confirm the archive's filename encoding in this extraction step; the original archive has been preserved."
                    + Environment.NewLine + diagnostic.StandardError);
            return diagnostic;
        }
    }
}
