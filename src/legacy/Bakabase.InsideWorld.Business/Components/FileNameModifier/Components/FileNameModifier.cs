using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Bakabase.InsideWorld.Business.Components.FileNameModifier.Models;
using Bootstrap.Extensions;

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
                FileNameModifierOperationType.Replace => !string.IsNullOrEmpty(operation.Text) ||
                                                         !string.IsNullOrEmpty(operation.TargetText),
                FileNameModifierOperationType.ChangeCase => true,
                FileNameModifierOperationType.AddAlphabetSequence => operation.AlphabetCount > 0,
                FileNameModifierOperationType.Reverse => true,
                _ => false
            };
        }

        private string ApplyOperation(string fileName, FileNameModifierOperation operation)
        {
            return ApplyOperationToText(fileName, operation);
        }

        private string GetTargetText(string fileName, FileNameModifierFileNameTarget target)
        {
            switch (target)
            {
                case FileNameModifierFileNameTarget.FileName:
                    return fileName;
                case FileNameModifierFileNameTarget.FileNameWithoutExtension:
                    return System.IO.Path.GetFileNameWithoutExtension(fileName);
                case FileNameModifierFileNameTarget.Extension:
                    return System.IO.Path.GetExtension(fileName);
                case FileNameModifierFileNameTarget.ExtensionWithoutDot:
                    var extension = System.IO.Path.GetExtension(fileName);
                    return extension.StartsWith(".") ? extension.Substring(1) : extension;
                default:
                    return fileName;
            }
        }

        private string ReplaceTargetText(string fileName, FileNameModifierFileNameTarget target, string newText)
        {
            switch (target)
            {
                case FileNameModifierFileNameTarget.FileName:
                    return newText;
                case FileNameModifierFileNameTarget.FileNameWithoutExtension:
                    var extension = System.IO.Path.GetExtension(fileName);
                    return newText + extension;
                case FileNameModifierFileNameTarget.Extension:
                    var nameWithoutExt = System.IO.Path.GetFileNameWithoutExtension(fileName);
                    return nameWithoutExt + newText;
                case FileNameModifierFileNameTarget.ExtensionWithoutDot:
                    var nameWithoutExt2 = System.IO.Path.GetFileNameWithoutExtension(fileName);
                    return nameWithoutExt2 + "." + newText;
                default:
                    return fileName;
            }
        }

        private string ApplyOperationToText(string fileName, FileNameModifierOperation operation)
        {
            // 先根据 Target 拆分出目标文本
            var text = GetTargetText(fileName, operation.Target);
            if (operation.ReplaceEntire)
            {
                var ext = System.IO.Path.GetExtension(fileName);
                if (!string.IsNullOrEmpty(ext) && (operation.Text == null || !operation.Text.EndsWith(ext)))
                    return (operation.Text ?? text) + ext;
                return operation.Text ?? text;
            }
            return operation.Operation switch
            {
                FileNameModifierOperationType.Insert => ApplyInsert(text, operation),
                FileNameModifierOperationType.AddDateTime => ApplyAddDateTime(text, operation),
                FileNameModifierOperationType.Delete => ApplyDelete(text, operation),
                FileNameModifierOperationType.Replace => ApplyReplace(text, operation),
                FileNameModifierOperationType.ChangeCase => ApplyChangeCase(text, operation),
                FileNameModifierOperationType.AddAlphabetSequence => ApplyAddAlphabetSequence(text, operation),
                FileNameModifierOperationType.Reverse => ApplyReverse(text, operation),
                _ => text
            };
        }

        private string ApplyInsert(string text, FileNameModifierOperation operation)
        {
            if (operation.Text == null) return text;
            switch (operation.Position)
            {
                case FileNameModifierPosition.Start:
                    return operation.Text + text;
                case FileNameModifierPosition.End:
                    return text + operation.Text;
                case FileNameModifierPosition.AtPosition:
                    if (operation.PositionIndex >= 0 && operation.PositionIndex <= text.Length)
                        return text.Insert(operation.PositionIndex, operation.Text);
                    return text;
                case FileNameModifierPosition.AfterText:
                    if (string.IsNullOrEmpty(operation.TargetText)) return text;
                    var indexAfter = text.IndexOf(operation.TargetText, StringComparison.Ordinal);
                    if (indexAfter >= 0)
                        return text.Insert(indexAfter + operation.TargetText.Length, operation.Text);
                    return text;
                case FileNameModifierPosition.BeforeText:
                    if (string.IsNullOrEmpty(operation.TargetText)) return text;
                    var indexBefore = text.IndexOf(operation.TargetText, StringComparison.Ordinal);
                    if (indexBefore >= 0)
                        return text.Insert(indexBefore, operation.Text);
                    return text;
                default:
                    return text;
            }
        }

        private string ApplyAddDateTime(string text, FileNameModifierOperation operation)
        {
            var dateTimeText = DateTime.Now.ToString(operation.DateTimeFormat ?? "yyyyMMdd_HHmmss");
            switch (operation.Position)
            {
                case FileNameModifierPosition.Start:
                    return dateTimeText + text;
                case FileNameModifierPosition.End:
                    return text + dateTimeText;
                case FileNameModifierPosition.AtPosition:
                    if (operation.PositionIndex >= 0 && operation.PositionIndex <= text.Length)
                        return text.Insert(operation.PositionIndex, dateTimeText);
                    return text;
                default:
                    return text;
            }
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

        private string ApplyReplace(string text, FileNameModifierOperation operation)
        {
            if (operation.ReplaceEntire)
                return operation.Text ?? text;
            else
                return string.IsNullOrEmpty(operation.TargetText)
                    ? text
                    : text.Replace(operation.TargetText, operation.Text ?? text);
        }

        private string ApplyChangeCase(string text, FileNameModifierOperation operation)
        {
            switch (operation.CaseType)
            {
                case FileNameModifierCaseType.TitleCase:
                    return ToTitleCase(text);
                case FileNameModifierCaseType.UpperCase:
                    return text.ToUpper();
                case FileNameModifierCaseType.LowerCase:
                    return text.ToLower();
                case FileNameModifierCaseType.CamelCase:
                    return ToCamelCase(text);
                case FileNameModifierCaseType.PascalCase:
                    return ToPascalCase(text);
                default:
                    return text;
            }
        }

        private string ApplyAddAlphabetSequence(string text, FileNameModifierOperation operation)
        {
            var alphabetSequence = GenerateAlphabetSequence(operation.AlphabetStartChar, operation.AlphabetCount);
            switch (operation.Position)
            {
                case FileNameModifierPosition.Start:
                    return alphabetSequence + text;
                case FileNameModifierPosition.End:
                    return text + alphabetSequence;
                case FileNameModifierPosition.AtPosition:
                    if (operation.PositionIndex >= 0 && operation.PositionIndex <= text.Length)
                        return text.Insert(operation.PositionIndex, alphabetSequence);
                    return text;
                default:
                    return text;
            }
        }

        private string ApplyReverse(string text, FileNameModifierOperation operation)
        {
            return new string(text.Reverse().ToArray());
        }

        private string ToTitleCase(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;
            return char.ToUpper(text[0]) + text.Substring(1).ToLower();
        }

        private string ToCamelCase(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;
            var words = text.Split(new[] { ' ', '_', '-', '.' }, StringSplitOptions.RemoveEmptyEntries);
            var result = new StringBuilder();
            for (int i = 0; i < words.Length; i++)
            {
                if (i == 0)
                    result.Append(words[i].ToLower());
                else
                    result.Append(char.ToUpper(words[i][0]) + words[i].Substring(1).ToLower());
            }

            return result.ToString();
        }

        private string ToPascalCase(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;
            var words = text.Split([' ', '_', '-', '.'], StringSplitOptions.RemoveEmptyEntries);
            var result = new StringBuilder();
            foreach (var word in words)
                result.Append(char.ToUpper(word[0]) + word.Substring(1).ToLower());
            return result.ToString();
        }

        private string GenerateAlphabetSequence(char startChar, int count)
        {
            var result = new StringBuilder();
            var currentChar = startChar;
            for (int i = 0; i < count; i++)
            {
                result.Append(currentChar);
                currentChar = (char)(currentChar + 1);
            }

            return result.ToString();
        }
    }
}