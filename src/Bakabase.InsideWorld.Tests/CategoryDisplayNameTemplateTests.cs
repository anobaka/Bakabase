using System;
using System.Collections.Generic;
using Bakabase.InsideWorld.Business.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;

namespace Bakabase.InsideWorld.Tests;

[TestClass]
public class CategoryDisplayNameTemplateTests
{
    /// <summary>
    /// Gets or sets the test context which provides
    /// information about and functionality for the current test run.
    /// </summary>
    public TestContext TestContext { get; set; }

    [TestMethod]
    public void Test()
    {
        var template = "[{作者]{名称}({原作})[{语言}]";
        var propertyMatchers = new Dictionary<string, string?>
        {
            ["{作者}"] = "",
            ["名称"] = "测试名称",
            ["原作"] = "测试原作",
            ["语言"] = "测试语言"
        };

        var wrappers = new (string Left, string Right)[]
        {
            ("[", "]"),
            ("(", ")"),
            ("{", "}"),
        };

        var segments = CategoryHelpers.SplitDisplayNameTemplateIntoSegments(template, propertyMatchers, wrappers);
        TestContext.WriteLine(JsonConvert.SerializeObject(segments, Formatting.Indented));
    }
}