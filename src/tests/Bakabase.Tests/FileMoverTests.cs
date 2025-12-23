using System.IO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.Orm;
using Bakabase.InsideWorld.Business.Components;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.FileMover;
using Bakabase.InsideWorld.Business.Components.ReservedProperty;
using Bakabase.Modules.Property.Extensions;
using Bootstrap.Components.Orm.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.Tests.Implementations;
using Bootstrap.Components.Configuration;
using Bootstrap.Components.Logging.LogService.Extensions;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Aliyun.OSS.Model;
using Bakabase.Abstractions.Extensions;
using Bakabase.Infrastructures.Components.Gui;
using Bakabase.Infrastructures.Components.Logging;
using Bakabase.Infrastructures.Components.Orm.Log;
using Bakabase.Tests.Utils;
using Bootstrap.Components.Logging.LogService.Services;
using FluentAssertions.Common;

namespace Bakabase.Tests;

[TestClass]
public class FileMoverTests
{
    [TestMethod]
    public async Task Test()
    {
        var sp = await TestServiceBuilder.BuildServiceProvider();
        var fm = sp.GetRequiredService<TestFileMover>();

        var root = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!,
            $"Bakabase.Tests.FileMover.{DateTime.Now:yyyyMMddHHmmssfff}");
        Directory.CreateDirectory(root);

        var relativeFsEntries = new string[]
        {
            @"d1",
            @"d1\f1",
            @"d1\d1\f1",
            @"d1\d1\d1\d1",
            @"d2",
            @"d3",
            // @"d3\f1",
            @"d3\f2",
            @"d4\f1-fresh",
            @"d4\d1\f1",
            @"d5"
        };

        var rand = new Random();

        var fileBytesMap = new Dictionary<string, byte[]>();

        foreach (var relativeFs in relativeFsEntries)
        {
            var fs = Path.Combine(root, relativeFs);
            var segments = fs.Split('\\');
            var filename = segments.Last();
            if (filename.StartsWith("f"))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(fs)!);
                var bytes = new byte[5555];
                for (var i = 0; i < bytes.Length; i++)
                {
                    bytes[i] = (byte) rand.Next(0, 255);
                }

                await File.WriteAllBytesAsync(fs, bytes);
                fileBytesMap[fs] = bytes;
                fm.SetCreationTime(fs, !filename.EndsWith("fresh") ? DateTime.Now.AddMinutes(-20) : DateTime.Now);
            }
            else
            {
                Directory.CreateDirectory(fs);
            }
        }

        var targetSourcesMap = new Dictionary<string, HashSet<string>>
        {
            {"d1-3", ["d1", "d2", "d3"]},
            {"d4-5", ["d4", "d5"]}
        };

        var testOptions = new FileSystemOptions.FileMoverOptions
        {
            Targets = targetSourcesMap.Select(ts => new FileSystemOptions.FileMoverOptions.Target
            {
                Path = Path.Combine(root, ts.Key),
                Sources = ts.Value.Select(x => Path.Combine(root, x)).ToList()
            }).ToList(),
            Delay = TimeSpan.FromMinutes(5)
        };

        var expectedFsMap = testOptions.Targets.SelectMany(x =>
        {
            var map = new Dictionary<string, byte[]?>();
            foreach (var source in x.Sources)
            {
                var fs = Directory.GetFileSystemEntries(source);
                foreach (var f in fs)
                {
                    map.TryAdd(f.Replace(source, x.Path).StandardizePath()!, fileBytesMap.GetValueOrDefault(f));
                }
            }

            return map.Where(x => !x.Key.EndsWith("fresh")).Select(a => (Target: a.Key, Bytes: a.Value));
        }).ToDictionary(d => d.Target, d => d.Bytes);

        await fm.TestMoving(testOptions);

        var actualFsMap = testOptions.Targets.SelectMany(x =>
        {
            var fs = Directory.GetFileSystemEntries(x.Path);
            return fs.Select(f => (Path: f.StandardizePath()!, Bytes: File.Exists(f) ? File.ReadAllBytes(f) : null));
        }).ToDictionary(d => d.Path, d => d.Bytes);

        var missingEntries = expectedFsMap.Where(x => !actualFsMap.ContainsKey(x.Key)).ToList();
        missingEntries.Should().BeNullOrEmpty();
        var extraEntries = actualFsMap.Where(x => !expectedFsMap.ContainsKey(x.Key)).ToList();
        extraEntries.Should().BeNullOrEmpty();

        foreach (var (k, v) in expectedFsMap)
        {
            v.Should().Equal(actualFsMap[k]);
        }
    }
}