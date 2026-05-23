using System;
using System.Collections.Generic;
using Bakabase.InsideWorld.Business.Components.FileNameModifier.Components;
using Bakabase.InsideWorld.Business.Components.FileNameModifier.Models;

namespace Bakabase.Tests;

[TestClass]
public class FileNameModifierTests
{
    private readonly FileNameModifier _modifier = new();

    [TestMethod]
    [DataRow("abc.txt", "PRE_abc.txt", FileNameModifierOperationType.Insert, FileNameModifierPosition.Start, "PRE_", null)]
    [DataRow("abc.txt", "abc.txtPRE_", FileNameModifierOperationType.Insert, FileNameModifierPosition.End, "PRE_", null)]
    [DataRow("abc.txt", "aPRE_bc.txt", FileNameModifierOperationType.Insert, FileNameModifierPosition.AtPosition, "PRE_", 1)]
    [DataRow("abc.txt", "aPRE_bc.txt", FileNameModifierOperationType.Insert, FileNameModifierPosition.AfterText, "PRE_", null, "a")] // After 'a'
    [DataRow("abc.txt", "PRE_abc.txt", FileNameModifierOperationType.AddDateTime, FileNameModifierPosition.Start, null, null, null, "yyyy", true)]
    [DataRow("abc.txt", "abc.txtPRE_", FileNameModifierOperationType.AddDateTime, FileNameModifierPosition.End, null, null, null, "PRE_", false)]
    [DataRow("abcdef.txt", "abef.txt", FileNameModifierOperationType.Delete, FileNameModifierPosition.Start, null, null, null, null, false, 2, 2)]
    [DataRow("abc.txt", "xyz.txt", FileNameModifierOperationType.Replace, FileNameModifierPosition.Start, "xyz", null, null, null, false, 0, 0, true)]
    [DataRow("abc.txt", "ABC.TXT", FileNameModifierOperationType.ChangeCase, FileNameModifierPosition.Start, null, null, null, null, false, 0, 0, false, FileNameModifierCaseType.UpperCase)]
    [DataRow("abc.txt", "Aabc.txt", FileNameModifierOperationType.AddAlphabetSequence, FileNameModifierPosition.Start, null, null, null, null, false, 0, 0, false, FileNameModifierCaseType.TitleCase, 'A', 1)]
    [DataRow("abc.txt", "txt.cba", FileNameModifierOperationType.Reverse, FileNameModifierPosition.Start)]
    public void Test_ModifyFileNames(
        string input,
        string expected,
        FileNameModifierOperationType opType,
        FileNameModifierPosition pos,
        string? text = null,
        int? posIndex = null,
        string? targetText = null,
        string? dateTimeFormat = null,
        bool addDateTime = false,
        int deleteStart = 0,
        int deleteCount = 0,
        bool replaceEntire = false,
        FileNameModifierCaseType caseType = FileNameModifierCaseType.TitleCase,
        char alphabetStart = 'A',
        int alphabetCount = 0
    )
    {
        var op = new FileNameModifierOperation
        {
            Operation = opType,
            Position = pos,
            Text = text,
            PositionIndex = posIndex ?? 0,
            TargetText = targetText,
            DateTimeFormat = dateTimeFormat,
            DeleteStartPosition = deleteStart,
            DeleteCount = deleteCount,
            ReplaceEntire = replaceEntire,
            CaseType = caseType,
            AlphabetStartChar = alphabetStart,
            AlphabetCount = alphabetCount,
            Target = FileNameModifierFileNameTarget.FileName // 显式指定
        };
        var result = _modifier.ModifyFileNames(new List<string> { input }, new List<FileNameModifierOperation> { op });
        if (addDateTime && dateTimeFormat != null)
        {
            StringAssert.StartsWith(result[0], DateTime.Now.ToString(dateTimeFormat));
        }
        else
        {
            Assert.AreEqual(expected, result[0]);
        }
    }

    [TestMethod]
    public void Test_ModifyFileNames_WithDirectories()
    {
        // Test with directory paths
        var directoryNames = new List<string> { "MyDocuments", "Photos", "Videos" };
        var operations = new List<FileNameModifierOperation>
        {
            new FileNameModifierOperation
            {
                Target = FileNameModifierFileNameTarget.FileName,
                Operation = FileNameModifierOperationType.Insert,
                Position = FileNameModifierPosition.Start,
                Text = "Backup_"
            }
        };
        
        var result = _modifier.ModifyFileNames(directoryNames, operations);
        
        Assert.AreEqual(3, result.Count);
        Assert.AreEqual("Backup_MyDocuments", result[0]);
        Assert.AreEqual("Backup_Photos", result[1]);
        Assert.AreEqual("Backup_Videos", result[2]);
    }

    [TestMethod]
    public void Test_ModifyFileNames_MixedFilesAndDirectories()
    {
        // Test with mixed file and directory names
        var names = new List<string> { "document.txt", "Photos", "video.mp4", "Documents" };
        var operations = new List<FileNameModifierOperation>
        {
            new FileNameModifierOperation
            {
                Target = FileNameModifierFileNameTarget.FileName,
                Operation = FileNameModifierOperationType.ChangeCase,
                CaseType = FileNameModifierCaseType.UpperCase
            }
        };
        
        var result = _modifier.ModifyFileNames(names, operations);
        
        Assert.AreEqual(4, result.Count);
        Assert.AreEqual("DOCUMENT.TXT", result[0]);
        Assert.AreEqual("PHOTOS", result[1]);
        Assert.AreEqual("VIDEO.MP4", result[2]);
        Assert.AreEqual("DOCUMENTS", result[3]);
    }

    [TestMethod]
    public void Test_ValidateOperation()
    {
        // Insert 需要 Text
        Assert.IsFalse(_modifier.ValidateOperation(new FileNameModifierOperation { Operation = FileNameModifierOperationType.Insert, Text = null }));
        Assert.IsTrue(_modifier.ValidateOperation(new FileNameModifierOperation { Operation = FileNameModifierOperationType.Insert, Text = "abc" }));
        // AddDateTime 需要 DateTimeFormat
        Assert.IsFalse(_modifier.ValidateOperation(new FileNameModifierOperation { Operation = FileNameModifierOperationType.AddDateTime, DateTimeFormat = null }));
        Assert.IsTrue(_modifier.ValidateOperation(new FileNameModifierOperation { Operation = FileNameModifierOperationType.AddDateTime, DateTimeFormat = "yyyy" }));
        // Delete 需要 DeleteCount>0, DeleteStartPosition>=0
        Assert.IsFalse(_modifier.ValidateOperation(new FileNameModifierOperation { Operation = FileNameModifierOperationType.Delete, DeleteCount = 0, DeleteStartPosition = 0 }));
        Assert.IsTrue(_modifier.ValidateOperation(new FileNameModifierOperation { Operation = FileNameModifierOperationType.Delete, DeleteCount = 1, DeleteStartPosition = 0 }));
        // Replace 需要 ReplaceEntire 或 TargetText
        Assert.IsFalse(_modifier.ValidateOperation(new FileNameModifierOperation { Operation = FileNameModifierOperationType.Replace, Text = null, TargetText = null, ReplaceEntire = false }));
        Assert.IsFalse(_modifier.ValidateOperation(new FileNameModifierOperation { Operation = FileNameModifierOperationType.Replace, Text = "a", TargetText = null, ReplaceEntire = false })); // Text 不足以使 Replace 有效
        Assert.IsTrue(_modifier.ValidateOperation(new FileNameModifierOperation { Operation = FileNameModifierOperationType.Replace, Text = null, TargetText = "b" }));
        Assert.IsTrue(_modifier.ValidateOperation(new FileNameModifierOperation { Operation = FileNameModifierOperationType.Replace, ReplaceEntire = true }));
        // AddAlphabetSequence 需要 AlphabetCount>0
        Assert.IsFalse(_modifier.ValidateOperation(new FileNameModifierOperation { Operation = FileNameModifierOperationType.AddAlphabetSequence, AlphabetCount = 0 }));
        Assert.IsTrue(_modifier.ValidateOperation(new FileNameModifierOperation { Operation = FileNameModifierOperationType.AddAlphabetSequence, AlphabetCount = 2 }));
    }

    [TestMethod]
    public void Test_All_Positions_And_Targets()
    {
        var fileName = "abc.def.txt";
        var ops = new List<FileNameModifierOperation>();
        // Insert at all positions
        foreach (FileNameModifierPosition pos in Enum.GetValues(typeof(FileNameModifierPosition)))
        {
            ops.Add(new FileNameModifierOperation { Operation = FileNameModifierOperationType.Insert, Position = pos, Text = "X", PositionIndex = 1, TargetText = "b", Target = FileNameModifierFileNameTarget.FileName });
        }
        var results = _modifier.ModifyFileNames(new List<string> { fileName }, ops);
        // 当前 FileNameModifier 实现为多操作依次叠加，最终只返回一个结果
        Assert.AreEqual(1, results.Count);
        // AddDateTime at all positions
        ops.Clear();
        foreach (FileNameModifierPosition pos in Enum.GetValues(typeof(FileNameModifierPosition)))
        {
            ops.Add(new FileNameModifierOperation { Operation = FileNameModifierOperationType.AddDateTime, Position = pos, DateTimeFormat = "yyyy", PositionIndex = 1, Target = FileNameModifierFileNameTarget.FileName });
        }
        results = _modifier.ModifyFileNames(new List<string> { fileName }, ops);
        // 当前 FileNameModifier 实现为多操作依次叠加，最终只返回一个结果
        Assert.AreEqual(1, results.Count);
        // AddAlphabetSequence at all positions
        ops.Clear();
        foreach (FileNameModifierPosition pos in Enum.GetValues(typeof(FileNameModifierPosition)))
        {
            ops.Add(new FileNameModifierOperation { Operation = FileNameModifierOperationType.AddAlphabetSequence, Position = pos, AlphabetStartChar = 'Z', AlphabetCount = 2, PositionIndex = 1, Target = FileNameModifierFileNameTarget.FileName });
        }
        results = _modifier.ModifyFileNames(new List<string> { fileName }, ops);
        // 当前 FileNameModifier 实现为多操作依次叠加，最终只返回一个结果
        Assert.AreEqual(1, results.Count);
    }

    [TestMethod]
    public void Test_Replace_Combinations()
    {
        var fileName = "abc.txt";
        // ReplaceEntire true
        var op = new FileNameModifierOperation { Operation = FileNameModifierOperationType.Replace, ReplaceEntire = true, Text = "xyz.txt", Target = FileNameModifierFileNameTarget.FileName };
        var result = _modifier.ModifyFileNames(new List<string> { fileName }, new List<FileNameModifierOperation> { op });
        Assert.AreEqual("xyz.txt", result[0]);
        // Replace by TargetText
        op = new FileNameModifierOperation { Operation = FileNameModifierOperationType.Replace, ReplaceEntire = false, Text = "X", TargetText = "a", Target = FileNameModifierFileNameTarget.FileName };
        result = _modifier.ModifyFileNames(new List<string> { fileName }, new List<FileNameModifierOperation> { op });
        Assert.AreEqual("Xbc.txt", result[0]);
        // Replace by TargetText not found
        op = new FileNameModifierOperation { Operation = FileNameModifierOperationType.Replace, ReplaceEntire = false, Text = "X", TargetText = "notfound", Target = FileNameModifierFileNameTarget.FileName };
        result = _modifier.ModifyFileNames(new List<string> { fileName }, new List<FileNameModifierOperation> { op });
        Assert.AreEqual("abc.txt", result[0]);
    }

    [TestMethod]
    public void Test_Replace_WithRegex()
    {
        // 简单正则替换
        var fileName = "abc123def456.txt";
        var op = new FileNameModifierOperation
        {
            Operation = FileNameModifierOperationType.Replace,
            TargetText = @"\d+",
            Text = "X",
            Regex = true,
            Target = FileNameModifierFileNameTarget.FileName
        };
        var result = _modifier.ModifyFileNames(new List<string> { fileName }, new List<FileNameModifierOperation> { op });
        Assert.AreEqual("abcXdefX.txt", result[0]);

        // 正则替换删除匹配内容（替换为空）
        op = new FileNameModifierOperation
        {
            Operation = FileNameModifierOperationType.Replace,
            TargetText = @"\d+",
            Text = "",
            Regex = true,
            Target = FileNameModifierFileNameTarget.FileName
        };
        result = _modifier.ModifyFileNames(new List<string> { fileName }, new List<FileNameModifierOperation> { op });
        Assert.AreEqual("abcdef.txt", result[0]);

        // 使用捕获组
        fileName = "file_2024_01_15.txt";
        op = new FileNameModifierOperation
        {
            Operation = FileNameModifierOperationType.Replace,
            TargetText = @"(\d{4})_(\d{2})_(\d{2})",
            Text = "$1-$2-$3",
            Regex = true,
            Target = FileNameModifierFileNameTarget.FileName
        };
        result = _modifier.ModifyFileNames(new List<string> { fileName }, new List<FileNameModifierOperation> { op });
        Assert.AreEqual("file_2024-01-15.txt", result[0]);

        // 无效正则表达式应返回原文本
        fileName = "abc.txt";
        op = new FileNameModifierOperation
        {
            Operation = FileNameModifierOperationType.Replace,
            TargetText = @"[invalid(regex",
            Text = "X",
            Regex = true,
            Target = FileNameModifierFileNameTarget.FileName
        };
        result = _modifier.ModifyFileNames(new List<string> { fileName }, new List<FileNameModifierOperation> { op });
        Assert.AreEqual("abc.txt", result[0]);

        // Regex = false 时应使用普通字符串替换
        fileName = "a.b.c.txt";
        op = new FileNameModifierOperation
        {
            Operation = FileNameModifierOperationType.Replace,
            TargetText = ".",
            Text = "_",
            Regex = false,
            Target = FileNameModifierFileNameTarget.FileName
        };
        result = _modifier.ModifyFileNames(new List<string> { fileName }, new List<FileNameModifierOperation> { op });
        Assert.AreEqual("a_b_c_txt", result[0]);

        // Regex = true 时 "." 匹配任意字符
        op = new FileNameModifierOperation
        {
            Operation = FileNameModifierOperationType.Replace,
            TargetText = ".",
            Text = "_",
            Regex = true,
            Target = FileNameModifierFileNameTarget.FileName
        };
        result = _modifier.ModifyFileNames(new List<string> { fileName }, new List<FileNameModifierOperation> { op });
        Assert.AreEqual("_________", result[0]);
    }

    [TestMethod]
    public void Test_ChangeCase_AllTypes()
    {
        var fileName = "abc.txt";
        foreach (FileNameModifierCaseType caseType in Enum.GetValues(typeof(FileNameModifierCaseType)))
        {
            var op = new FileNameModifierOperation { Operation = FileNameModifierOperationType.ChangeCase, CaseType = caseType, Target = FileNameModifierFileNameTarget.FileName };
            var result = _modifier.ModifyFileNames(new List<string> { fileName }, new List<FileNameModifierOperation> { op });
            Assert.IsNotNull(result[0]);
        }
    }

    [TestMethod]
    public void Test_Target_AllTypes()
    {
        var fileName = "abc.def.txt";
        var ops = new List<FileNameModifierOperation>();
        foreach (FileNameModifierFileNameTarget target in Enum.GetValues(typeof(FileNameModifierFileNameTarget)))
        {
            var op = new FileNameModifierOperation
            {
                Operation = FileNameModifierOperationType.Insert,
                Position = FileNameModifierPosition.Start,
                Text = "X",
                Target = target // 显式指定
            };
            ops.Add(op);
        }
        var results = _modifier.ModifyFileNames(new List<string> { fileName }, ops);
        // 当前 FileNameModifier 实现为多操作依次叠加，最终只返回一个结果
        Assert.AreEqual(1, results.Count);
    }

    [TestMethod]
    public void Test_Multi_Operations()
    {
        var fileName = "abc.txt";
        var ops = new List<FileNameModifierOperation> {
            new FileNameModifierOperation { Operation = FileNameModifierOperationType.Insert, Position = FileNameModifierPosition.Start, Text = "A", Target = FileNameModifierFileNameTarget.FileName },
            new FileNameModifierOperation { Operation = FileNameModifierOperationType.ChangeCase, CaseType = FileNameModifierCaseType.UpperCase, Target = FileNameModifierFileNameTarget.FileName },
            new FileNameModifierOperation { Operation = FileNameModifierOperationType.Reverse, Target = FileNameModifierFileNameTarget.FileName }
        };
        var result = _modifier.ModifyFileNames(new List<string> { fileName }, ops);
        Assert.IsNotNull(result[0]);
    }

    [TestMethod]
    public void Test_Special_And_Empty_Cases()
    {
        // 空字符串
        var op = new FileNameModifierOperation { Operation = FileNameModifierOperationType.Insert, Position = FileNameModifierPosition.Start, Text = "X", Target = FileNameModifierFileNameTarget.FileName };
        var result = _modifier.ModifyFileNames(new List<string> { "" }, new List<FileNameModifierOperation> { op });
        Assert.AreEqual("X", result[0]);
        // 仅扩展名
        result = _modifier.ModifyFileNames(new List<string> { ".gitignore" }, new List<FileNameModifierOperation> { op });
        StringAssert.StartsWith(result[0], "X");
        // 多扩展名
        result = _modifier.ModifyFileNames(new List<string> { "a.b.c.txt" }, new List<FileNameModifierOperation> { op });
        StringAssert.StartsWith(result[0], "X");
        // 非ASCII
        result = _modifier.ModifyFileNames(new List<string> { "测试.txt" }, new List<FileNameModifierOperation> { op });
        StringAssert.StartsWith(result[0], "X");
        // 极端长度
        var longName = new string('a', 255) + ".txt";
        result = _modifier.ModifyFileNames(new List<string> { longName }, new List<FileNameModifierOperation> { op });
        StringAssert.StartsWith(result[0], "X");
    }

    [TestMethod]
    public void Test_Exceptional_Positions()
    {
        var fileName = "abc.txt";
        // AtPosition 越界
        var op = new FileNameModifierOperation { Operation = FileNameModifierOperationType.Insert, Position = FileNameModifierPosition.AtPosition, Text = "X", PositionIndex = 100, Target = FileNameModifierFileNameTarget.FileName };
        var result = _modifier.ModifyFileNames(new List<string> { fileName }, new List<FileNameModifierOperation> { op });
        Assert.AreEqual(fileName, result[0]);
        // AfterText/BeforeText找不到
        op = new FileNameModifierOperation { Operation = FileNameModifierOperationType.Insert, Position = FileNameModifierPosition.AfterText, Text = "X", TargetText = "notfound", Target = FileNameModifierFileNameTarget.FileName };
        result = _modifier.ModifyFileNames(new List<string> { fileName }, new List<FileNameModifierOperation> { op });
        Assert.AreEqual(fileName, result[0]);
        op = new FileNameModifierOperation { Operation = FileNameModifierOperationType.Insert, Position = FileNameModifierPosition.BeforeText, Text = "X", TargetText = "notfound", Target = FileNameModifierFileNameTarget.FileName };
        result = _modifier.ModifyFileNames(new List<string> { fileName }, new List<FileNameModifierOperation> { op });
        Assert.AreEqual(fileName, result[0]);
    }
} 