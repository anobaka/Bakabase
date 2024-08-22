﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Resource.Components.PlayableFileSelector.Infrastructures;
using Bakabase.InsideWorld.Models.Configs.CustomOptions;

namespace Bakabase.InsideWorld.Business.Components.Resource.Components.PlayableFileSelector
{
    [PlayableFileSelector(OptionsType = typeof(ExtensionBasedPlayableFileSelectorOptions),
        Name = nameof(ExtensionBasedPlayableFileSelector),
        Description = "Select files with specific extensions as playable files.")]
    public class ExtensionBasedPlayableFileSelector : IPlayableFileSelector
    {
        protected ExtensionBasedPlayableFileSelectorOptions Options { get; }

        public ExtensionBasedPlayableFileSelector(ExtensionBasedPlayableFileSelectorOptions options)
        {
            Options = options;
        }

        public Task<string> Validate()
        {
            return Task.FromResult((string) null);
        }

        public virtual Task<string[]> GetStartFiles(string fileOrDirectory, CancellationToken ct)
        {
            // Relative - Fullname
            try
            {
                var attr = File.GetAttributes(fileOrDirectory);
                var files = attr.HasFlag(FileAttributes.Directory)
                    ? Directory.GetFiles(fileOrDirectory, "*.*", SearchOption.AllDirectories)
                    : new[] {fileOrDirectory};

                var extensions = Options.Extensions;
                var standardRoot = new DirectoryInfo(fileOrDirectory).FullName;
                var targetFilesMap = files
                    .Where(a => extensions.Contains(Path.GetExtension(a), StringComparer.OrdinalIgnoreCase))
                    .ToDictionary(a => a.Replace(standardRoot, null).TrimStart(Path.DirectorySeparatorChar), t => t);
                var startFiles = targetFilesMap.OrderBy(a => a.Key).Take(Options.MaxFileCount).Select(a => a.Value)
                    .ToArray();
                return Task.FromResult(startFiles);
            }
            catch
            {
                return Task.FromResult(Array.Empty<string>());
            }
        }
    }
}