using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Bakabase.InsideWorld.Business.Components.FileNameModifier.Models;

namespace Bakabase.InsideWorld.Business.Components.FileNameModifier.Components
{
    /// <summary>
    /// 文件名修改器组件
    /// 支持批量修改文件名，包括文件名、扩展名等维度的各种操作
    /// </summary>
    public class FileNameModifier : Abstractions.IFileNameModifier
    {
        public List<string> ModifyFileNames(List<string> fileNames, List<FileNameModifierOperation> operations)
        {
            var result = new List<string>();
            foreach (var fileName in fileNames)
            {
                var modified = fileName;
                foreach (var op in operations)
                {
                    modified = ApplyOperation(modified, op);
                }
                result.Add(modified);
            }
            return result;
        }

        public string PreviewModification(string fileName, List<FileNameModifierOperation> operations)
        {
            var modifiedFileName = fileName;
            foreach (var operation in operations)
            {
                modifiedFileName = ApplyOperation(modifiedFileName, operation);
            }

            return modifiedFileName;
        }

        public bool ValidateOperation(FileNameModifierOperation operation)
        {
            return operation.Operation switch
            {
                FileNameModifierOperationType.Insert => !string.IsNullOrEmpty(operation.Text),
                FileNameModifierOperationType.AddDateTime => !string.IsNullOrEmpty(operation.DateTimeFormat),
                FileNameModifierOperationType.Delete => operation is { DeleteCount: > 0, DeleteStartPosition: >= 0 },
                FileNameModifierOperationType.Replace => operation.ReplaceEntire || !string.IsNullOrEmpty(operation.TargetText),
                FileNameModifierOperationType.ChangeCase => true,
                FileNameModifierOperationType.AddAlphabetSequence => operation.AlphabetCount > 0,
                FileNameModifierOperationType.Reverse => true,
                _ => false
            };
        }

        private string ApplyOperation(string fileName, FileNameModifierOperation operation)
        {
            // 先根据 Target 拆分出目标文本
            var text = GetTargetText(fileName, operation.Target);

            var result = operation.Operation switch
            {
                FileNameModifierOperationType.Insert => ApplyInsert(text, operation),
                FileNameModifierOperationType.AddDateTime => ApplyAddDateTime(text, operation),
                FileNameModifierOperationType.Delete => ApplyDelete(text, operation),
                FileNameModifierOperationType.Replace => ApplyReplace(text, fileName, operation),
                FileNameModifierOperationType.ChangeCase => ApplyChangeCase(text, operation),
                FileNameModifierOperationType.AddAlphabetSequence => ApplyAddAlphabetSequence(text, operation),
                FileNameModifierOperationType.Reverse => ApplyReverse(text),
                _ => text
            };

            // 如果 target 是 FileNameWithoutExtension，自动拼接扩展名
            if (operation.Target == FileNameModifierFileNameTarget.FileNameWithoutExtension)
            {
                var ext = System.IO.Path.GetExtension(fileName);
                return result + ext;
            }
            return result;
        }

        private string GetTargetText(string fileName, FileNameModifierFileNameTarget target)
        {
            return target switch
            {
                FileNameModifierFileNameTarget.FileName => fileName,
                FileNameModifierFileNameTarget.FileNameWithoutExtension => System.IO.Path.GetFileNameWithoutExtension(fileName),
                FileNameModifierFileNameTarget.Extension => System.IO.Path.GetExtension(fileName),
                FileNameModifierFileNameTarget.ExtensionWithoutDot => System.IO.Path.GetExtension(fileName).TrimStart('.'),
                _ => fileName
            };
        }

        /// <summary>
        /// 通用的文本插入方法，根据 Position 在指定位置插入文本
        /// </summary>
        private string InsertTextAtPosition(string text, string insertText, FileNameModifierOperation operation)
        {
            if (string.IsNullOrEmpty(insertText))
                return text;

            return operation.Position switch
            {
                FileNameModifierPosition.Start => insertText + text,
                FileNameModifierPosition.End => text + insertText,
                FileNameModifierPosition.AtPosition when operation.PositionIndex >= 0 && operation.PositionIndex <= text.Length
                    => text.Insert(operation.PositionIndex, insertText),
                FileNameModifierPosition.AfterText when !string.IsNullOrEmpty(operation.TargetText)
                    => text.IndexOf(operation.TargetText, StringComparison.Ordinal) is var idx and >= 0
                        ? text.Insert(idx + operation.TargetText.Length, insertText)
                        : text,
                FileNameModifierPosition.BeforeText when !string.IsNullOrEmpty(operation.TargetText)
                    => text.IndexOf(operation.TargetText, StringComparison.Ordinal) is var idx and >= 0
                        ? text.Insert(idx, insertText)
                        : text,
                _ => text
            };
        }

        private string ApplyInsert(string text, FileNameModifierOperation operation)
        {
            return InsertTextAtPosition(text, operation.Text ?? "", operation);
        }

        private string ApplyAddDateTime(string text, FileNameModifierOperation operation)
        {
            var dateTimeText = DateTime.Now.ToString(operation.DateTimeFormat ?? "yyyyMMdd_HHmmss");
            return InsertTextAtPosition(text, dateTimeText, operation);
        }

        private string ApplyDelete(string text, FileNameModifierOperation operation)
        {
            if (operation.DeleteStartPosition >= 0 && operation.DeleteStartPosition < text.Length &&
                operation.DeleteCount > 0)
            {
                var endPosition = Math.Min(operation.DeleteStartPosition + operation.DeleteCount, text.Length);
                return text.Remove(operation.DeleteStartPosition, endPosition - operation.DeleteStartPosition);
            }

            return text;
        }

        private string ApplyReplace(string text, string fileName, FileNameModifierOperation operation)
        {
            // ReplaceEntire: 替换整个目标文本
            if (operation.ReplaceEntire)
            {
                var ext = System.IO.Path.GetExtension(fileName);
                var newText = operation.Text ?? text;
                // 如果有扩展名且新文本不包含该扩展名，则自动添加
                if (!string.IsNullOrEmpty(ext) && !newText.EndsWith(ext))
                    return newText + ext;
                return newText;
            }

            if (string.IsNullOrEmpty(operation.TargetText))
                return text;

            var replacement = operation.Text ?? "";

            if (operation.Regex)
            {
                try
                {
                    return Regex.Replace(text, operation.TargetText, replacement);
                }
                catch (ArgumentException)
                {
                    // 无效的正则表达式，返回原文本
                    return text;
                }
            }

            return text.Replace(operation.TargetText, replacement);
        }

        private string ApplyChangeCase(string text, FileNameModifierOperation operation)
        {
            return operation.CaseType switch
            {
                FileNameModifierCaseType.TitleCase => ToTitleCase(text),
                FileNameModifierCaseType.UpperCase => text.ToUpper(),
                FileNameModifierCaseType.LowerCase => text.ToLower(),
                FileNameModifierCaseType.CamelCase => ToCamelCase(text),
                FileNameModifierCaseType.PascalCase => ToPascalCase(text),
                _ => text
            };
        }

        private string ApplyAddAlphabetSequence(string text, FileNameModifierOperation operation)
        {
            var alphabetSequence = GenerateAlphabetSequence(operation.AlphabetStartChar, operation.AlphabetCount);
            return InsertTextAtPosition(text, alphabetSequence, operation);
        }

        private string ApplyReverse(string text)
        {
            return new string(text.Reverse().ToArray());
        }

        private static string ToTitleCase(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;
            return char.ToUpper(text[0]) + text.Substring(1).ToLower();
        }

        private static string ToCamelCase(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;
            var words = text.Split([' ', '_', '-', '.'], StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0)
                return text;

            var result = new StringBuilder();
            for (int i = 0; i < words.Length; i++)
            {
                var word = words[i];
                if (word.Length == 0) continue;

                if (i == 0)
                    result.Append(word.ToLower());
                else
                    result.Append(char.ToUpper(word[0])).Append(word.Length > 1 ? word.Substring(1).ToLower() : "");
            }

            return result.Length > 0 ? result.ToString() : text;
        }

        private static string ToPascalCase(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;
            var words = text.Split([' ', '_', '-', '.'], StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0)
                return text;

            var result = new StringBuilder();
            foreach (var word in words)
            {
                if (word.Length == 0) continue;
                result.Append(char.ToUpper(word[0])).Append(word.Length > 1 ? word.Substring(1).ToLower() : "");
            }

            return result.Length > 0 ? result.ToString() : text;
        }

        private static string GenerateAlphabetSequence(char startChar, int count)
        {
            if (count <= 0)
                return string.Empty;

            var result = new StringBuilder(count);
            var isUpper = char.IsUpper(startChar);
            var baseChar = isUpper ? 'A' : 'a';
            var offset = char.ToUpper(startChar) - 'A';

            // 确保 offset 在有效范围内
            if (offset < 0 || offset > 25)
                offset = 0;

            for (int i = 0; i < count; i++)
            {
                result.Append((char)(baseChar + (offset + i) % 26));
            }

            return result.ToString();
        }
    }
}