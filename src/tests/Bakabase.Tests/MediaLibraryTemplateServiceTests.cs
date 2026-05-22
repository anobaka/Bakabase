using System;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Input;
using Bakabase.Abstractions.Services;
using Bakabase.TestKit.Utils;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

/// <summary>
/// Integration coverage for MediaLibraryTemplateService CRUD — a previously untested
/// (but still actively used) service: add / get / get-all / get-by-keys / put / delete /
/// duplicate.
/// </summary>
[TestClass]
public sealed class MediaLibraryTemplateServiceTests
{
    private IServiceProvider _sp = null!;
    private IMediaLibraryTemplateService _service = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _sp = await TestServiceBuilder.BuildServiceProvider();
        _service = _sp.GetRequiredService<IMediaLibraryTemplateService>();
    }

    [TestMethod]
    public async Task Add_ThenGet_ReturnsTemplate()
    {
        var added = await _service.Add(new MediaLibraryTemplateAddInputModel("My Template"));
        var fetched = await _service.Get(added.Id);
        Assert.AreEqual("My Template", fetched.Name);
    }

    [TestMethod]
    public async Task GetAll_ReturnsEveryTemplate()
    {
        await _service.Add(new MediaLibraryTemplateAddInputModel("A"));
        await _service.Add(new MediaLibraryTemplateAddInputModel("B"));
        Assert.AreEqual(2, (await _service.GetAll()).Length);
    }

    [TestMethod]
    public async Task GetByKeys_ReturnsRequestedTemplates()
    {
        var a = await _service.Add(new MediaLibraryTemplateAddInputModel("A"));
        await _service.Add(new MediaLibraryTemplateAddInputModel("B"));
        var fetched = await _service.GetByKeys([a.Id]);
        Assert.AreEqual(1, fetched.Length);
        Assert.AreEqual("A", fetched[0].Name);
    }

    [TestMethod]
    public async Task Put_UpdatesTemplate()
    {
        var added = await _service.Add(new MediaLibraryTemplateAddInputModel("Before"));
        var template = await _service.Get(added.Id);
        template.Name = "After";
        await _service.Put(added.Id, template);

        Assert.AreEqual("After", (await _service.Get(added.Id)).Name);
    }

    [TestMethod]
    public async Task Delete_RemovesTemplate()
    {
        var added = await _service.Add(new MediaLibraryTemplateAddInputModel("Doomed"));
        await _service.Delete(added.Id);
        Assert.AreEqual(0, (await _service.GetAll()).Length);
    }

    [TestMethod]
    public async Task Duplicate_AddsACopy()
    {
        var added = await _service.Add(new MediaLibraryTemplateAddInputModel("Original"));
        await _service.Duplicate(added.Id);
        Assert.AreEqual(2, (await _service.GetAll()).Length);
    }
}
