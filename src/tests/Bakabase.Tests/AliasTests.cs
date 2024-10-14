using System;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Modules.Alias.Abstractions.Models.Db;
using Bakabase.Modules.Alias.Abstractions.Services;
using Bakabase.Modules.Alias.Models.Domain;
using Bakabase.Tests.Utils;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests;

[TestClass]
public class AliasTests
{
    [TestMethod]
    public async Task TestMergeGroupsByImporting()
    {
        var sp = await HostUtils.PrepareScopedServiceProvider();
        var aliasService = sp.GetRequiredService<IAliasService>();

        var initGroups = new string[][]
        {
            ["a", "a1", "a2", "a2"],
            ["c", "c1", "c2"],
            ["e", "e1", "e2"],
            ["f", "f1"]
        };

        var newGroups = new string[][]
        {
            ["a", "a1", "a2", "a3"],
            ["b", "b1", "b2"],
            ["c"],
            ["e", "f", "f1"]
        };

        await aliasService.MergeGroups(initGroups);

        var task = async () => await aliasService.MergeGroups(newGroups);
        await task.Should().ThrowAsync<AliasException>();

        var expectedAliases = new string[][]
        {
            ["a", "a1", "a2", "a3"],
            ["b", "b1", "b2"],
            ["c", "c1", "c2"],
            ["e", "e1", "e2"],
            ["f", "f1"]
        };
        var actualAliases = (await aliasService.GetAll()).GroupBy(d => d.Preferred ?? d.Text).Select(a =>
            new[] {a.Key}.Concat(a.Where(b => b.Text != a.Key).Select(b => b.Text).ToArray())).ToArray();

        actualAliases.Should().BeEquivalentTo(expectedAliases);
    }

    [TestMethod]
    public async Task TestExport()
    {
        var sp = await HostUtils.PrepareScopedServiceProvider();
        var aliasService = sp.GetRequiredService<IAliasService>();

        var initGroups = new string[][]
        {
            ["a", "a1", "a2", "a2"],
            ["c", "c1", "c2"],
            ["e", "e1", "e2"],
            ["f", "f1"]
        };

        await aliasService.MergeGroups(initGroups);

        var expectedAliases = new string[][]
        {
            ["a", "a1", "a2"],
            ["c", "c1", "c2"],
            ["e", "e1", "e2"],
            ["f", "f1"]
        };
        var actualAliases = (await aliasService.GetAll()).GroupBy(d => d.Preferred ?? d.Text).Select(a =>
            new[] { a.Key }.Concat(a.Where(b => b.Text != a.Key).Select(b => b.Text).ToArray())).ToArray();

        actualAliases.Should().BeEquivalentTo(expectedAliases);
    }
}