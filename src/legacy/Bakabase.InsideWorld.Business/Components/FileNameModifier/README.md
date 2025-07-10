# 文件名修改器组件 (FileNameModifier)

## 概述

文件名修改器组件是一个用于批量修改文件名的工具，支持多种维度和操作类型。该组件可以处理文件名、扩展名等不同维度的修改，并提供丰富的操作选项。

## 功能特性

### 支持的目标区域 (FileNameTarget)

- **FullFileName**: 完整文件名（包含扩展名）
- **FileNameWithoutExtension**: 文件名（不含扩展名）
- **ExtensionWithDot**: 扩展名（包含分隔符）
- **ExtensionWithoutDot**: 扩展名（不包含分隔符）

### 支持的操作类型 (OperationType)

- **Insert**: 插入文本
- **AddDateTime**: 添加时间日期
- **Delete**: 删除字符
- **Replace**: 替换文本
- **ChangeCase**: 转换大小写
- **AddAlphabetSequence**: 制作字母序号列表
- **Reverse**: 反向

### 支持的位置 (Position)

- **Start**: 开头
- **End**: 结尾
- **AtPosition**: 指定位置
- **AfterCharacter**: 指定字符后
- **BeforeCharacter**: 指定字符前

### 支持的大小写类型 (CaseType)

- **TitleCase**: 首字母大写
- **UpperCase**: 全部大写
- **LowerCase**: 全部小写
- **CamelCase**: 驼峰命名
- **PascalCase**: 帕斯卡命名

## 使用方法

### 基本用法

```csharp
var modifier = new FileNameModifier();
var fileNames = new List<string> { "document.txt", "image.jpg" };

// 在文件名前添加前缀
var operations = new List<ModificationOperation>
{
    new ModificationOperation
    {
        Target = FileNameTarget.FileNameWithoutExtension,
        Operation = OperationType.Insert,
        Position = Position.Start,
        Text = "backup_"
    }
};

var result = modifier.ModifyFileNames(fileNames, operations);
```

### 复杂操作示例

```csharp
// 删除下划线，转换为驼峰命名，添加序号
var operations = new List<ModificationOperation>
{
    // 删除下划线
    new ModificationOperation
    {
        Target = FileNameTarget.FileNameWithoutExtension,
        Operation = OperationType.Replace,
        TargetCharacter = "_",
        Text = ""
    },
    
    // 转换为驼峰命名
    new ModificationOperation
    {
        Target = FileNameTarget.FileNameWithoutExtension,
        Operation = OperationType.ChangeCase,
        CaseType = CaseType.CamelCase
    },
    
    // 在开头添加序号
    new ModificationOperation
    {
        Target = FileNameTarget.FileNameWithoutExtension,
        Operation = OperationType.AddAlphabetSequence,
        Position = Position.Start,
        AlphabetStartChar = 'A',
        AlphabetCount = 1
    }
};
```

### 预览功能

```csharp
var fileName = "test_document.txt";
var operations = new List<ModificationOperation> { /* 操作配置 */ };
var preview = modifier.PreviewModification(fileName, operations);
Console.WriteLine($"预览结果: {fileName} -> {preview}");
```

### 验证功能

```csharp
var operation = new ModificationOperation { /* 操作配置 */ };
bool isValid = modifier.ValidateOperation(operation);
```

## 操作配置详解

### Insert 操作

用于在指定位置插入文本。

```csharp
new ModificationOperation
{
    Target = FileNameTarget.FileNameWithoutExtension,
    Operation = OperationType.Insert,
    Position = Position.Start, // 或 End, AtPosition, AfterCharacter, BeforeCharacter
    Text = "prefix_",
    PositionIndex = 0, // 用于 AtPosition
    TargetCharacter = "_" // 用于 AfterCharacter/BeforeCharacter
}
```

### AddDateTime 操作

用于添加时间日期。

```csharp
new ModificationOperation
{
    Target = FileNameTarget.FileNameWithoutExtension,
    Operation = OperationType.AddDateTime,
    Position = Position.End,
    DateTimeFormat = "yyyyMMdd_HHmmss"
}
```

### Delete 操作

用于删除指定位置的字符。

```csharp
new ModificationOperation
{
    Target = FileNameTarget.FileNameWithoutExtension,
    Operation = OperationType.Delete,
    DeleteStartPosition = 0,
    DeleteCount = 5
}
```

### Replace 操作

用于替换文本。

```csharp
new ModificationOperation
{
    Target = FileNameTarget.FileNameWithoutExtension,
    Operation = OperationType.Replace,
    TargetCharacter = "_",
    Text = "-",
    ReplaceEntire = false // 是否替换整个内容
}
```

### ChangeCase 操作

用于转换大小写。

```csharp
new ModificationOperation
{
    Target = FileNameTarget.FileNameWithoutExtension,
    Operation = OperationType.ChangeCase,
    CaseType = CaseType.UpperCase // 或 LowerCase, TitleCase, CamelCase, PascalCase
}
```

### AddAlphabetSequence 操作

用于添加字母序号列表。

```csharp
new ModificationOperation
{
    Target = FileNameTarget.FileNameWithoutExtension,
    Operation = OperationType.AddAlphabetSequence,
    Position = Position.Start,
    AlphabetStartChar = 'A',
    AlphabetCount = 3 // 生成 A, B, C
}
```

### Reverse 操作

用于反向文本。

```csharp
new ModificationOperation
{
    Target = FileNameTarget.FileNameWithoutExtension,
    Operation = OperationType.Reverse
}
```

## 注意事项

1. 操作按顺序执行，前一个操作的结果会作为下一个操作的输入
2. 对于扩展名操作，建议使用 `ExtensionWithoutDot` 目标区域以避免重复的点号
3. 删除操作时要注意索引范围，避免越界
4. 时间日期格式遵循 .NET 的 DateTime 格式化规则
5. 字母序号从指定字符开始，按字母表顺序递增

## 扩展性

该组件采用接口设计，可以轻松扩展新的操作类型和维度。只需实现 `IFileNameModifier` 接口即可创建自定义的文件名修改器。 