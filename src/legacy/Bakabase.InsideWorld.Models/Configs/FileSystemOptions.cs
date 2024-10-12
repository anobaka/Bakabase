﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.Configurations;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bootstrap.Components.Configuration.Abstractions;

namespace Bakabase.InsideWorld.Models.Configs
{
    [Options]
    public class FileSystemOptions
    {
        public string[]? RecentMovingDestinations { get; set; }
        public FileMoverOptions? FileMover { get; set; }
        public FileProcessorOptions? FileProcessor { get; set; }

        public void AddRecentMovingDestination(string destination)
        {
            const int capacity = 5;

            RecentMovingDestinations ??= [];
            var paths = RecentMovingDestinations.Where(x => x != destination).ToList();
            paths.Insert(0, destination);
            RecentMovingDestinations = paths.Take(capacity).ToArray();
        }

        public class FileMoverOptions
        {
            /// <summary>
            /// In asp.net core 7, colon can not be used in configuration key, so we use array instead.
            /// https://github.com/dotnet/runtime/issues/67616
            /// </summary>
            public List<Target>? Targets { get; set; }

            public bool Enabled { get; set; }

            public TimeSpan Delay { get; set; } = TimeSpan.FromMinutes(5);

            public record Target
            {
                public string Path { get; set; } = string.Empty;
                public List<string> Sources { get; set; } = new();
            }
        }

        public record FileProcessorOptions
        {
            public string WorkingDirectory { get; set; } = string.Empty;
        }
    }
}