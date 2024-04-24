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

namespace Bakabase.InsideWorld.Business.Components.Compression
{
    public class CompressedFileService
    {
        private string _sevenZExecutable => Path.Combine(_env.ContentRootPath, "libs/7z.exe");
        private readonly IWebHostEnvironment _env;

        public CompressedFileService(IWebHostEnvironment env)
        {
            _env = env;
        }

        public async Task ExtractToCurrentDirectory(string compressedFilePath, bool overwrite, CancellationToken ct)
        {
            var password = compressedFilePath.GetPasswordsFromPath().FirstOrDefault();
            var dir = Path.GetDirectoryName(compressedFilePath);

            var arguments = new List<string>
            {
                password.IsNotEmpty() ? $"-p{password}" : null!,
                overwrite ? "-y" : null!,
                $"-o{dir}",
                "x", compressedFilePath
            }.Where(a => a.IsNotEmpty()).ToArray();

            var command = Cli.Wrap(_sevenZExecutable).WithArguments(arguments, true);

            var result = await command.ExecuteAsync(ct);
            if (result.ExitCode != 0)
            {
                throw new Exception($"Got exit code: {result.ExitCode} while executing [{command}]");
            }
        }

        public async Task<MemoryStream> ExtractOneEntry(string compressedFilePath, string entryPath,
            CancellationToken ct)
        {
            var password = compressedFilePath.GetPasswordsFromPath().FirstOrDefault();

            var arguments = new List<string>
            {
                password.IsNotEmpty() ? $"-p{password}" : null!,
                "-so",
                "e", compressedFilePath,
                entryPath
            }.Where(a => a.IsNotEmpty()).ToArray();

            var ms = new MemoryStream();
            var command = Cli.Wrap(_sevenZExecutable).WithArguments(arguments, true)
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
                var password = compressFilePath.GetPasswordsFromPath().FirstOrDefault();
                var sb = new StringBuilder();

                var arguments = new List<string>
                {
                    password.IsNotEmpty() ? $"-p{password}" : null!,
                    "l", compressFilePath
                }.Where(a => a.IsNotEmpty()).ToArray();

                var command = Cli.Wrap(_sevenZExecutable)
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
    }
}