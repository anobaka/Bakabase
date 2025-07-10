using System;
using System.Collections.Generic;
using Bakabase.InsideWorld.Business.Components.FileNameModifier.Models;

namespace Bakabase.InsideWorld.Business.Components.FileNameModifier.Components
{
    /// <summary>
    /// 文件名修改器使用示例
    /// </summary>
    public class FileNameModifierExample
    {
        public static void BasicUsageExample()
        {
            var modifier = new FileNameModifier();
            var fileNames = new List<string>
            {
                "document.txt",
                "image.jpg",
                "video.mp4"
            };
            var operations1 = new List<FileNameModifierOperation>
            {
                new FileNameModifierOperation
                {
                    Target = FileNameModifierFileNameTarget.FileNameWithoutExtension,
                    Operation = FileNameModifierOperationType.Insert,
                    Position = FileNameModifierPosition.Start,
                    Text = "backup_"
                }
            };
            var result1 = modifier.ModifyFileNames(fileNames, operations1);
            Console.WriteLine("示例1 - 添加前缀:");
            foreach (var fileName in result1)
            {
                Console.WriteLine(fileName);
            }
            var operations2 = new List<FileNameModifierOperation>
            {
                new FileNameModifierOperation
                {
                    Target = FileNameModifierFileNameTarget.FileNameWithoutExtension,
                    Operation = FileNameModifierOperationType.ChangeCase,
                    CaseType = FileNameModifierCaseType.UpperCase
                }
            };
            var result2 = modifier.ModifyFileNames(fileNames, operations2);
            Console.WriteLine("\n示例2 - 转换为大写:");
            foreach (var fileName in result2)
            {
                Console.WriteLine(fileName);
            }
            var operations3 = new List<FileNameModifierOperation>
            {
                new FileNameModifierOperation
                {
                    Target = FileNameModifierFileNameTarget.FileNameWithoutExtension,
                    Operation = FileNameModifierOperationType.AddDateTime,
                    Position = FileNameModifierPosition.End,
                    DateTimeFormat = "yyyyMMdd"
                }
            };
            var result3 = modifier.ModifyFileNames(fileNames, operations3);
            Console.WriteLine("\n示例3 - 添加时间戳:");
            foreach (var fileName in result3)
            {
                Console.WriteLine(fileName);
            }
        }
        public static void ComplexOperationsExample()
        {
            var modifier = new FileNameModifier();
            var fileNames = new List<string>
            {
                "my_document_v1.txt",
                "old_image_photo.jpg",
                "sample_video_clip.mp4"
            };
            var operations = new List<FileNameModifierOperation>
            {
                new FileNameModifierOperation
                {
                    Target = FileNameModifierFileNameTarget.FileNameWithoutExtension,
                    Operation = FileNameModifierOperationType.Replace,
                    TargetText = "_",
                    Text = ""
                },
                new FileNameModifierOperation
                {
                    Target = FileNameModifierFileNameTarget.FileNameWithoutExtension,
                    Operation = FileNameModifierOperationType.ChangeCase,
                    CaseType = FileNameModifierCaseType.CamelCase
                },
                new FileNameModifierOperation
                {
                    Target = FileNameModifierFileNameTarget.FileNameWithoutExtension,
                    Operation = FileNameModifierOperationType.AddAlphabetSequence,
                    Position = FileNameModifierPosition.Start,
                    AlphabetStartChar = 'A',
                    AlphabetCount = 1
                }
            };
            var result = modifier.ModifyFileNames(fileNames, operations);
            Console.WriteLine("复杂操作示例:");
            foreach (var fileName in result)
            {
                Console.WriteLine(fileName);
            }
        }
        public static void ExtensionOperationsExample()
        {
            var modifier = new FileNameModifier();
            var fileNames = new List<string>
            {
                "document.txt",
                "image.jpg",
                "video.mp4"
            };
            var operations = new List<FileNameModifierOperation>
            {
                new FileNameModifierOperation
                {
                    Target = FileNameModifierFileNameTarget.ExtensionWithoutDot,
                    Operation = FileNameModifierOperationType.ChangeCase,
                    CaseType = FileNameModifierCaseType.UpperCase
                }
            };
            var result = modifier.ModifyFileNames(fileNames, operations);
            Console.WriteLine("扩展名操作示例:");
            foreach (var fileName in result)
            {
                Console.WriteLine(fileName);
            }
        }
        public static void PreviewExample()
        {
            var modifier = new FileNameModifier();
            var fileName = "test_document.txt";
            var operations = new List<FileNameModifierOperation>
            {
                new FileNameModifierOperation
                {
                    Target = FileNameModifierFileNameTarget.FileNameWithoutExtension,
                    Operation = FileNameModifierOperationType.Insert,
                    Position = FileNameModifierPosition.Start,
                    Text = "backup_"
                },
                new FileNameModifierOperation
                {
                    Target = FileNameModifierFileNameTarget.FileNameWithoutExtension,
                    Operation = FileNameModifierOperationType.ChangeCase,
                    CaseType = FileNameModifierCaseType.TitleCase
                }
            };
            var preview = modifier.PreviewModification(fileName, operations);
            Console.WriteLine($"预览结果: {fileName} -> {preview}");
        }
        public static void ValidationExample()
        {
            var modifier = new FileNameModifier();
            var validOperation = new FileNameModifierOperation
            {
                Target = FileNameModifierFileNameTarget.FileNameWithoutExtension,
                Operation = FileNameModifierOperationType.Insert,
                Position = FileNameModifierPosition.Start,
                Text = "prefix_"
            };
            Console.WriteLine($"有效操作验证: {modifier.ValidateOperation(validOperation)}");
            var invalidOperation = new FileNameModifierOperation
            {
                Target = FileNameModifierFileNameTarget.FileNameWithoutExtension,
                Operation = FileNameModifierOperationType.Insert,
                Position = FileNameModifierPosition.Start,
                Text = ""
            };
            Console.WriteLine($"无效操作验证: {modifier.ValidateOperation(invalidOperation)}");
        }
    }
} 