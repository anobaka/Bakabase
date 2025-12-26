# StandardValue Module Guide

## Overview

`Bakabase.Modules.StandardValue` defines 9 standard data formats with serialization, conversion, and validation capabilities.

## StandardValue Types

| Type | CLR Type | Serialization Format |
|------|----------|---------------------|
| String | `string` | Raw string |
| ListString | `List<string>` | Comma-separated with escape |
| Decimal | `decimal` | Numeric string |
| Link | `LinkValue` | `text,url` escaped |
| Boolean | `bool` | `true`/`false` |
| DateTime | `DateTime` | Unix timestamp (ms) |
| Time | `TimeSpan` | Milliseconds |
| ListListString | `List<List<string>>` | `;` outer, `,` inner |
| ListTag | `List<TagValue>` | `group,name;...` |

## Creating Values (Type-Safe)

### Using StandardValueFactory (Recommended)

```csharp
using Bakabase.Modules.StandardValue.Components;

// String
var str = StandardValueFactory.String("hello");

// ListString
var list = StandardValueFactory.ListString("a", "b", "c");
var list2 = StandardValueFactory.ListString(existingList);

// Decimal
var num = StandardValueFactory.Decimal(123.45m);
var nullableNum = StandardValueFactory.Decimal(maybeNull); // returns null if input is null

// Link
var link = StandardValueFactory.Link("Google", "https://google.com");
var link2 = StandardValueFactory.Link(existingLinkValue);

// Boolean
var flag = StandardValueFactory.Boolean(true);

// DateTime
var dt = StandardValueFactory.DateTime(DateTime.Now);

// Time
var ts = StandardValueFactory.Time(TimeSpan.FromHours(2));

// ListListString (for Multilevel BizValue)
var paths = StandardValueFactory.ListListString(
    new List<string> { "Category", "SubCategory" },
    new List<string> { "Another", "Path" }
);

// ListTag (for Tags BizValue)
var tags = StandardValueFactory.ListTag(
    new TagValue("Group", "Tag1"),
    new TagValue(null, "Tag2")
);
// Or with tuples
var tags2 = StandardValueFactory.ListTag(
    ("Group", "Tag1"),
    (null, "Tag2")
);
```

### Using PropertySystem Entry Point

```csharp
using Bakabase.Modules.Property;

// Via PropertySystem.Value.Create
var str = PropertySystem.Value.Create.String("hello");
var list = PropertySystem.Value.Create.ListString("a", "b");
var tags = PropertySystem.Value.Create.ListTag(("Group", "Name"));
```

## Working with StandardValue<T>

```csharp
// StandardValue<T> is a typed wrapper
StandardValue<string> value = StandardValueFactory.String("hello");

// Get the typed value
string? typedValue = value.Value;

// Get as object (for APIs requiring object)
object? objValue = value.AsObject();

// Check if empty
bool isEmpty = value.IsEmpty;

// Get the type
StandardValueType type = value.Type;
```

## Serialization/Deserialization

```csharp
using Bakabase.Modules.StandardValue.Extensions;

// Serialize
string? serialized = myValue.SerializeAsStandardValue(StandardValueType.String);

// Deserialize
object? deserialized = serialized.DeserializeAsStandardValue(StandardValueType.String);

// Type-safe deserialize
string? typed = serialized.DeserializeAsStandardValue<string>(StandardValueType.String);

// Via PropertySystem
string? serialized = PropertySystem.Value.Serialize(value, StandardValueType.ListString);
object? deserialized = PropertySystem.Value.Deserialize(serialized, StandardValueType.ListString);
```

## Validation

```csharp
// Check if value matches type
bool isValid = value.IsStandardValueType(StandardValueType.String);

// Via PropertySystem
bool isValid = PropertySystem.Value.Validate(value, StandardValueType.String);

// Infer type from value
StandardValueType? type = myValue.InferStandardValueType();
StandardValueType? type = PropertySystem.Value.InferType(myValue);
StandardValueType? type = PropertySystem.Value.InferType<List<string>>();
```

## Conversion

```csharp
// Sync conversion (no custom datetime parsing)
object? converted = PropertySystem.Value.Convert(value, fromType, toType);

// Async conversion with custom datetime parsing (use IStandardValueService)
var converted = await standardValueService.Convert(value, fromType, toType);

// Get conversion rules
var rules = StandardValueSystem.GetConversionRule(fromType, toType);
// rules is a [Flags] enum showing what happens during conversion
```

## Key Files

| Purpose | Path |
|---------|------|
| Type enum | `src/abstractions/.../StandardValueType.cs` |
| Entry Point | `src/modules/Bakabase.Modules.StandardValue/StandardValueSystem.cs` |
| Factory | `src/modules/Bakabase.Modules.StandardValue/Components/StandardValueFactory.cs` |
| Handlers | `src/modules/Bakabase.Modules.StandardValue/Components/ValueHandlers/` |
| Extensions | `src/modules/Bakabase.Modules.StandardValue/Extensions/StandardValueExtensions.cs` |

## Best Practices

1. **Use StandardValueFactory** for type-safe value creation
2. **Check type before serialization** - mismatch causes data corruption
3. **Use PropertySystem.Value** as the unified entry point
4. **Check conversion rules** before type conversion to understand data loss
5. **Handle null cases** - most factory methods accept nullable inputs
