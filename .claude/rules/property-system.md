# Property System Guide

## Overview

`Bakabase.Modules.Property` defines 16 property types with DB/Biz value conversion, built-in property mappings, and type-safe accessors.

## Property Types & Value Mappings

| PropertyType | DbValueType | BizValueType | IsReference |
|-------------|-------------|--------------|-------------|
| SingleLineText | String | String | No |
| MultilineText | String | String | No |
| SingleChoice | String | String | Yes |
| MultipleChoice | ListString | ListString | Yes |
| Number | Decimal | Decimal | No |
| Percentage | Decimal | Decimal | No |
| Rating | Decimal | Decimal | No |
| Boolean | Boolean | Boolean | No |
| Link | Link | Link | No |
| Attachment | ListString | ListString | No |
| Date | DateTime | DateTime | No |
| DateTime | DateTime | DateTime | No |
| Time | Time | Time | No |
| Formula | String | String | No |
| Multilevel | ListString | ListListString | Yes |
| Tags | ListString | ListTag | Yes |

**Reference Types**: Store UUIDs in DB, display labels in Biz.

## Two-Layer Value System

```
Biz Value (user-facing) <-> PrepareDbValue/GetBizValue <-> DB Value (storage)
```

- **DbValue**: Stored in database, serialized as StandardValue
- **BizValue**: Used by business logic and UI, human-readable

## PropertySystem Entry Point

```csharp
using Bakabase.Modules.Property;
using Bakabase.Modules.Property.Components;

// === Value Creation ===
var str = PropertySystem.Value.Create.String("hello");
var list = PropertySystem.Value.Create.ListString("a", "b");

// === Property Value Factory (use PropertyValueFactory directly) ===
var dbValue = PropertyValueFactory.SingleChoice.Db(options, "label");
var bizValue = PropertyValueFactory.SingleChoice.Biz(options, dbValue);
var (tagDbValue, changed) = PropertyValueFactory.Tags.DbWithAutoCreate(options, tagList);

// === Built-in Properties (via PropertySystem.Builtin) ===
var mlProperty = PropertySystem.Builtin.Get(ResourceProperty.MediaLibraryV2Multi);
var rating = PropertySystem.Builtin.Rating;              // Reserved property
var filename = PropertySystem.Builtin.Filename;          // Internal property
var mediaLib = PropertySystem.Builtin.MediaLibraryV2Multi;

// === Property Info ===
var descriptor = PropertySystem.Property.GetDescriptor(PropertyType.Tags);
var dbType = PropertySystem.Property.GetDbValueType(PropertyType.Tags);
var bizType = PropertySystem.Property.GetBizValueType(PropertyType.Tags);
bool isRef = PropertySystem.Property.IsReferenceValueType(PropertyType.Tags);

// === Value Conversion ===
var bizValue = PropertySystem.Property.ToBizValue(property, dbValue);
var (dbValue, changed) = PropertySystem.Property.ToDbValue(property, bizValue);
```

## PropertyValueFactory (Type-Safe)

```csharp
using Bakabase.Modules.Property.Components;

// Text properties
var dbValue = PropertyValueFactory.SingleLineText.Db("hello");
var bizValue = PropertyValueFactory.MultilineText.Biz(dbValue);

// Choice properties
var dbValue = PropertyValueFactory.SingleChoice.Db(options, "Label");
var (dbValue, optionsChanged) = PropertyValueFactory.SingleChoice.DbWithAutoCreate(options, "NewLabel");
var bizValue = PropertyValueFactory.SingleChoice.Biz(options, dbValue);

// Multiple choice
var dbValue = PropertyValueFactory.MultipleChoice.Db(options, new[] { "Label1", "Label2" });
var bizValue = PropertyValueFactory.MultipleChoice.Biz(options, dbValue);

// Tags
var (dbValue, changed) = PropertyValueFactory.Tags.DbWithAutoCreate(options, tagValues);
var bizValue = PropertyValueFactory.Tags.Biz(options, dbValue);

// Multilevel
var (dbValue, changed) = PropertyValueFactory.Multilevel.DbWithAutoCreate(options, paths);
var bizValue = PropertyValueFactory.Multilevel.Biz(options, dbValue);

// Numbers, DateTime, etc.
var dbValue = PropertyValueFactory.Number.Db(123.45m);
var dbValue = PropertyValueFactory.DateTime.Db(DateTime.Now);
```

## Built-in Property Accessors

```csharp
using Bakabase.Modules.Property.Components;

// Internal properties
var filename = BuiltinProperties.Internal.Filename;
var createdAt = BuiltinProperties.Internal.CreatedAt;
var mediaLibrary = BuiltinProperties.Internal.MediaLibraryV2Multi; // Use this, not MediaLibraryV2

// Reserved properties
var rating = BuiltinProperties.Reserved.Rating;
var intro = BuiltinProperties.Reserved.Introduction;
var cover = BuiltinProperties.Reserved.Cover;

// Use accessor methods
var dbValue = filename.ToDbValue("test.txt");
var bizValue = createdAt.ToBizValue(dbValue);

// For choice properties
var dbValue = BuiltinProperties.Internal.MediaLibraryV2Multi.ToDbValue(options, labels);
var bizValue = BuiltinProperties.Internal.MediaLibraryV2Multi.ToBizValue(options, dbValue);
```

## Property Type Conversion

```csharp
// Using IPropertyTypeConverter (injected)
var result = await propertyTypeConverter.ConvertValueAsync(fromProperty, toProperty, dbValue);
// result.NewDbValue - converted value
// result.PropertyOptionsChanged - if options were modified
// result.UpdatedToProperty - property with updated options

// Preview conversion
var preview = await propertyTypeConverter.PreviewConversionAsync(fromProperty, toType, dbValues);
// preview.Changes - list of values that would change

// Batch conversion
var result = await propertyTypeConverter.ConvertValuesAsync(fromProperty, toProperty, dbValues);
```

## MediaLibraryV2 Migration

**IMPORTANT**: MediaLibraryV2 is transitioning to MediaLibraryV2Multi.

```csharp
using Bakabase.Modules.Property.Components;

// Check if legacy property
bool isLegacy = MediaLibraryV2Adapter.IsLegacyProperty(pool, id);

// Write: Single -> Multi
var multiDbValue = MediaLibraryV2Adapter.ToMultiDbValue(singleDbValue);

// Read: Multi -> Single (for backward compatibility)
var singleDbValue = MediaLibraryV2Adapter.ToSingleDbValue(multiDbValue);

// Read normalized (always List<string>)
var normalized = MediaLibraryV2Adapter.ReadAsMulti(dbValue);

// Get actual property for operations
var actualProperty = MediaLibraryV2Adapter.GetActualProperty(pool, id);
```

**For new code**: Use `ResourceProperty.MediaLibraryV2Multi` directly.

## Key Files

| Purpose | Path |
|---------|------|
| PropertySystem | `src/modules/Bakabase.Modules.Property/PropertySystem.cs` |
| PropertyValueFactory | `src/modules/Bakabase.Modules.Property/Components/PropertyValueFactory.cs` |
| BuiltinProperties | `src/modules/Bakabase.Modules.Property/Components/BuiltinProperties.cs` |
| IPropertyTypeConverter | `src/modules/Bakabase.Modules.Property/Abstractions/Components/IPropertyTypeConverter.cs` |
| PropertyTypeConverter | `src/modules/Bakabase.Modules.Property/Components/PropertyTypeConverter.cs` |
| MediaLibraryV2Adapter | `src/modules/Bakabase.Modules.Property/Components/MediaLibraryV2Adapter.cs` |
| PropertyInternals (internal) | `src/modules/Bakabase.Modules.Property/Components/PropertyInternals.cs` |
| Property descriptors | `src/modules/Bakabase.Modules.Property/Components/Properties/` |

## Best Practices

1. **Use PropertyValueFactory.{Type}.Db/Biz** for type-safe property value construction
2. **Use PropertySystem.Value.Create** for StandardValue creation
3. **Use PropertySystem.Builtin** or **BuiltinProperties** for Internal/Reserved property access
4. **Use IPropertyTypeConverter** for property type conversions (injected service)
5. **Use MediaLibraryV2Multi** instead of MediaLibraryV2 for new code
6. **Reference types store UUIDs** - never store display labels in DB
7. **Use DbWithAutoCreate** when you want options to be auto-added
