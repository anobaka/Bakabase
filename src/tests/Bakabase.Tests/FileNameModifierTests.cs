using System;
using System.Collections.Generic;
using Bakabase.InsideWorld.Business.Components.FileNameModifier.Components;
using Bakabase.InsideWorld.Business.Components.FileNameModifier.Models;
using Xunit;

namespace Bakabase.Tests;

public class FileNameModifierTests
{
    private readonly FileNameModifier _modifier = new();

    [Theory]
    [InlineData("abc.txt", "PRE_abc.txt", FileNameModifierOperationType.Insert, FileNameModifierPosition.Start, "PRE_", null)]
    [InlineData("abc.txt", "abc.txtPRE_", FileNameModifierOperationType.Insert, FileNameModifierPosition.End, "PRE_", null)]
    [InlineData("abc.txt", "aPRE_bc.txt", FileNameModifierOperationType.Insert, FileNameModifierPosition.AtPosition, "PRE_", 1)]
    [InlineData("abc.txt", "aPRE_bc.txt", FileNameModifierOperationType.Insert, FileNameModifierPosition.AfterText, "PRE_", null, "a")] // After 'a'
    [InlineData("abc.txt", "PRE_abc.txt", FileNameModifierOperationType.AddDateTime, FileNameModifierPosition.Start, null, null, null, "yyyy", true)]
    [InlineData("abc.txt", "abc.txtPRE_", FileNameModifierOperationType.AddDateTime, FileNameModifierPosition.End, null, null, null, "PRE_", false)]
    [InlineData("abcdef.txt", "abef.txt", FileNameModifierOperationType.Delete, FileNameModifierPosition.Start, null, null, null, null, false, 2, 2)]
    [InlineData("abc.txt", "xyz.txt", FileNameModifierOperationType.Replace, FileNameModifierPosition.Start, "xyz", null, null, null, false, 0, 0, true)]
    [InlineData("abc.txt", "ABC.TXT", FileNameModifierOperationType.ChangeCase, FileNameModifierPosition.Start, null, null, null, null, false, 0, 0, false, FileNameModifierCaseType.UpperCase)]
    [InlineData("abc.txt", "Aabc.txt", FileNameModifierOperationType.AddAlphabetSequence, FileNameModifierPosition.Start, null, null, null, null, false, 0, 0, false, FileNameModifierCaseType.TitleCase, 'A', 1)]
    [InlineData("abc.txt", "txt.cba", FileNameModifierOperationType.Reverse, FileNameModifierPosition.Start)]
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
            Assert.StartsWith(DateTime.Now.ToString(dateTimeFormat), result[0]);
        }
        else
        {
            Assert.Equal(expected, result[0]);
        }
    }

    [Fact]
    public void Test_ValidateOperation()
    {
        // Insert 需要 Text
        Assert.False(_modifier.ValidateOperation(new FileNameModifierOperation { Operation = FileNameModifierOperationType.Insert, Text = null }));
        Assert.True(_modifier.ValidateOperation(new FileNameModifierOperation { Operation = FileNameModifierOperationType.Insert, Text = "abc" }));
        // AddDateTime 需要 DateTimeFormat
        Assert.False(_modifier.ValidateOperation(new FileNameModifierOperation { Operation = FileNameModifierOperationType.AddDateTime, DateTimeFormat = null }));
        Assert.True(_modifier.ValidateOperation(new FileNameModifierOperation { Operation = FileNameModifierOperationType.AddDateTime, DateTimeFormat = "yyyy" }));
        // Delete 需要 DeleteCount>0, DeleteStartPosition>=0
        Assert.False(_modifier.ValidateOperation(new FileNameModifierOperation { Operation = FileNameModifierOperationType.Delete, DeleteCount = 0, DeleteStartPosition = 0 }));
        Assert.True(_modifier.ValidateOperation(new FileNameModifierOperation { Operation = FileNameModifierOperationType.Delete, DeleteCount = 1, DeleteStartPosition = 0 }));
        // Replace 需要 Text 或 TargetText
        Assert.False(_modifier.ValidateOperation(new FileNameModifierOperation { Operation = FileNameModifierOperationType.Replace, Text = null, TargetText = null }));
        Assert.True(_modifier.ValidateOperation(new FileNameModifierOperation { Operation = FileNameModifierOperationType.Replace, Text = "a", TargetText = null }));
        Assert.True(_modifier.ValidateOperation(new FileNameModifierOperation { Operation = FileNameModifierOperationType.Replace, Text = null, TargetText = "b" }));
        // AddAlphabetSequence 需要 AlphabetCount>0
        Assert.False(_modifier.ValidateOperation(new FileNameModifierOperation { Operation = FileNameModifierOperationType.AddAlphabetSequence, AlphabetCount = 0 }));
        Assert.True(_modifier.ValidateOperation(new FileNameModifierOperation { Operation = FileNameModifierOperationType.AddAlphabetSequence, AlphabetCount = 2 }));
    }

    [Fact]
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
        Assert.Equal(1, results.Count);
        // AddDateTime at all positions
        ops.Clear();
        foreach (FileNameModifierPosition pos in Enum.GetValues(typeof(FileNameModifierPosition)))
        {
            ops.Add(new FileNameModifierOperation { Operation = FileNameModifierOperationType.AddDateTime, Position = pos, DateTimeFormat = "yyyy", PositionIndex = 1, Target = FileNameModifierFileNameTarget.FileName });
        }
        results = _modifier.ModifyFileNames(new List<string> { fileName }, ops);
        // 当前 FileNameModifier 实现为多操作依次叠加，最终只返回一个结果
        Assert.Equal(1, results.Count);
        // AddAlphabetSequence at all positions
        ops.Clear();
        foreach (FileNameModifierPosition pos in Enum.GetValues(typeof(FileNameModifierPosition)))
        {
            ops.Add(new FileNameModifierOperation { Operation = FileNameModifierOperationType.AddAlphabetSequence, Position = pos, AlphabetStartChar = 'Z', AlphabetCount = 2, PositionIndex = 1, Target = FileNameModifierFileNameTarget.FileName });
        }
        results = _modifier.ModifyFileNames(new List<string> { fileName }, ops);
        // 当前 FileNameModifier 实现为多操作依次叠加，最终只返回一个结果
        Assert.Equal(1, results.Count);
    }

    [Fact]
    public void Test_Replace_Combinations()
    {
        var fileName = "abc.txt";
        // ReplaceEntire true
        var op = new FileNameModifierOperation { Operation = FileNameModifierOperationType.Replace, ReplaceEntire = true, Text = "xyz.txt", Target = FileNameModifierFileNameTarget.FileName };
        var result = _modifier.ModifyFileNames(new List<string> { fileName }, new List<FileNameModifierOperation> { op });
        Assert.Equal("xyz.txt", result[0]);
        // Replace by TargetText
        op = new FileNameModifierOperation { Operation = FileNameModifierOperationType.Replace, ReplaceEntire = false, Text = "X", TargetText = "a", Target = FileNameModifierFileNameTarget.FileName };
        result = _modifier.ModifyFileNames(new List<string> { fileName }, new List<FileNameModifierOperation> { op });
        Assert.Equal("Xbc.txt", result[0]);
        // Replace by TargetText not found
        op = new FileNameModifierOperation { Operation = FileNameModifierOperationType.Replace, ReplaceEntire = false, Text = "X", TargetText = "notfound", Target = FileNameModifierFileNameTarget.FileName };
        result = _modifier.ModifyFileNames(new List<string> { fileName }, new List<FileNameModifierOperation> { op });
        Assert.Equal("abc.txt", result[0]);
    }

    [Fact]
    public void Test_ChangeCase_AllTypes()
    {
        var fileName = "abc.txt";
        foreach (FileNameModifierCaseType caseType in Enum.GetValues(typeof(FileNameModifierCaseType)))
        {
            var op = new FileNameModifierOperation { Operation = FileNameModifierOperationType.ChangeCase, CaseType = caseType, Target = FileNameModifierFileNameTarget.FileName };
            var result = _modifier.ModifyFileNames(new List<string> { fileName }, new List<FileNameModifierOperation> { op });
            Assert.NotNull(result[0]);
        }
    }

    [Fact]
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
        Assert.Equal(1, results.Count);
    }

    [Fact]
    public void Test_Multi_Operations()
    {
        var fileName = "abc.txt";
        var ops = new List<FileNameModifierOperation> {
            new FileNameModifierOperation { Operation = FileNameModifierOperationType.Insert, Position = FileNameModifierPosition.Start, Text = "A", Target = FileNameModifierFileNameTarget.FileName },
            new FileNameModifierOperation { Operation = FileNameModifierOperationType.ChangeCase, CaseType = FileNameModifierCaseType.UpperCase, Target = FileNameModifierFileNameTarget.FileName },
            new FileNameModifierOperation { Operation = FileNameModifierOperationType.Reverse, Target = FileNameModifierFileNameTarget.FileName }
        };
        var result = _modifier.ModifyFileNames(new List<string> { fileName }, ops);
        Assert.NotNull(result[0]);
    }

    [Fact]
    public void Test_Special_And_Empty_Cases()
    {
        // 空字符串
        var op = new FileNameModifierOperation { Operation = FileNameModifierOperationType.Insert, Position = FileNameModifierPosition.Start, Text = "X", Target = FileNameModifierFileNameTarget.FileName };
        var result = _modifier.ModifyFileNames(new List<string> { "" }, new List<FileNameModifierOperation> { op });
        Assert.Equal("X", result[0]);
        // 仅扩展名
        result = _modifier.ModifyFileNames(new List<string> { ".gitignore" }, new List<FileNameModifierOperation> { op });
        Assert.StartsWith("X", result[0]);
        // 多扩展名
        result = _modifier.ModifyFileNames(new List<string> { "a.b.c.txt" }, new List<FileNameModifierOperation> { op });
        Assert.StartsWith("X", result[0]);
        // 非ASCII
        result = _modifier.ModifyFileNames(new List<string> { "测试.txt" }, new List<FileNameModifierOperation> { op });
        Assert.StartsWith("X", result[0]);
        // 极端长度
        var longName = new string('a', 255) + ".txt";
        result = _modifier.ModifyFileNames(new List<string> { longName }, new List<FileNameModifierOperation> { op });
        Assert.StartsWith("X", result[0]);
    }

    [Fact]
    public void Test_Exceptional_Positions()
    {
        var fileName = "abc.txt";
        // AtPosition 越界
        var op = new FileNameModifierOperation { Operation = FileNameModifierOperationType.Insert, Position = FileNameModifierPosition.AtPosition, Text = "X", PositionIndex = 100, Target = FileNameModifierFileNameTarget.FileName };
        var result = _modifier.ModifyFileNames(new List<string> { fileName }, new List<FileNameModifierOperation> { op });
        Assert.Equal(fileName, result[0]);
        // AfterText/BeforeText找不到
        op = new FileNameModifierOperation { Operation = FileNameModifierOperationType.Insert, Position = FileNameModifierPosition.AfterText, Text = "X", TargetText = "notfound", Target = FileNameModifierFileNameTarget.FileName };
        result = _modifier.ModifyFileNames(new List<string> { fileName }, new List<FileNameModifierOperation> { op });
        Assert.Equal(fileName, result[0]);
        op = new FileNameModifierOperation { Operation = FileNameModifierOperationType.Insert, Position = FileNameModifierPosition.BeforeText, Text = "X", TargetText = "notfound", Target = FileNameModifierFileNameTarget.FileName };
        result = _modifier.ModifyFileNames(new List<string> { fileName }, new List<FileNameModifierOperation> { op });
        Assert.Equal(fileName, result[0]);
    }
} 