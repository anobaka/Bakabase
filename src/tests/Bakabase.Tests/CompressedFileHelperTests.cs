using System;
using System.IO;
using System.Linq;
using Bakabase.InsideWorld.Business.Components.Compression;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests
{
    [TestClass]
    public class CompressedFileHelperTests
    {
        [TestMethod]
        public void Test()
        {
            var filenameList = new string[]
            {
                @"C:\123.txt",
                @"C:\dir\123.txt",

                @"C:\1.zip",
                @"C:\1.z01",
                @"C:\1.z02",

                @"C:\2.tar",

                @"C:\2.tar.gz",

                @"C:\2.rar.part1",
                @"C:\2.rar.part2",
                @"C:\2.rar.part3",


                @"C:\2.7z",

                @"C:\2.7z.00001",
                @"C:\2.7z.00002",
                @"C:\2.7z.00003",

                @"C:\2.r01",
                @"C:\2.r02",
                @"C:\2.rar",


                @"C:\dir\1.r01",
                @"C:\dir\1.r02",
                @"C:\dir\1.r03",
                @"C:\dir\1.r04"
            };


            var result = CompressedFileHelper.Group(filenameList);

            var str = string.Join(Environment.NewLine, result.Select(t => $"{t}{Environment.NewLine}"));

            const string rightStr = @"[Key]1
[Entry]C:/1.zip
[Part]C:/1.z01
[Part]C:/1.z02

[Key]2
[Entry]C:/2.rar
[Part]C:/2.r01
[Part]C:/2.r02

[Key]2
[Entry]C:/2.7z

[Key]2
[Entry]C:/2.tar

[Key]2
[Entry]C:/2.7z.00001
[Part]C:/2.7z.00002
[Part]C:/2.7z.00003

[Key]2
[Entry]C:/2.rar.part1
[Part]C:/2.rar.part2
[Part]C:/2.rar.part3

[Key]2
[Entry]C:/2.tar.gz
";

            Assert.AreEqual(rightStr, str);
        }
    }
}