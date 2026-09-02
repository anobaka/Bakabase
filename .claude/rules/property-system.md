# Property System Guide

## Overview

`Bakabase.Modules.Property` defines 16 property types with DB/Biz value conversion, built-in property mappings, and descriptor-driven type attributes.

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

**Reference Types**: Store option ids (UUIDs) in DB, display labels in Biz.

**Single source of truth**: this table is *generated from the descriptors* —
Db/BizValueType are inferred from each descriptor's generic arguments and
`IsReferenceValueType` is an abstract member every descriptor must implement
(omitting it fails the build). `PropertyAttributeMapGolden` locks the table.

## Two-Layer Value System

```
Biz Value (user-facing) <-> PrepareDbValue/GetBizValue <-> DB Value (storage)
```

- **DbValue**: Stored in database, serialized as StandardValue
- **BizValue**: Used by business logic and UI, human-readable

## PropertySystem Entry Point

```csharp
using Bakabase.Modules.Property;
using Bakabase.Modules.Property.Abstractions.Components;

// === Property Info (descriptor-derived) ===
var descriptor = PropertySystem.Property.GetDescriptor(PropertyType.Tags);
var dbType = PropertySystem.Property.GetDbValueType(PropertyType.Tags);
var bizType = PropertySystem.Property.GetBizValueType(PropertyType.Tags);
bool isRef = PropertySystem.Property.IsReferenceValueType(PropertyType.Tags);

// === Value Conversion ===
var bizValue = PropertySystem.Property.ToBizValue(property, dbValue);

// Write path (default): unmatched labels create options and mutate property.Options
var (dbValue, changed) = PropertySystem.Property.ToDbValue(property, bizValue);

// Read/validate path: never mutates options, unmatched entries are dropped
var (matched, _) = PropertySystem.Property.ToDbValue(property, bizValue,
    PropertyValueMatchPolicy.MatchOnly);

// === Built-in Properties ===
var p = PropertySystem.Builtin.Get(ResourceProperty.Rating);
var mla = PropertySystem.Builtin.MediaLibraryV2Multi; // int-based media library accessor
```

## Unified miss behavior

When a stored DbValue references an option that no longer exists, **every**
read path drops the entry (and returns null when nothing is left) — no path
leaks raw UUIDs to the UI. `DescriptorMissBehavior` tests lock this.

## PropertyValueFactory (reference types only)

Only Choice/Tags/Multilevel need a factory (their db/biz values differ).
`Match*` methods are thin wrappers over the descriptors — `addOnMiss` maps to
`PropertyValueMatchPolicy`.

```csharp
using Bakabase.Modules.Property.Components;

var dbValue = PropertyValueFactory.SingleChoice.MatchDbValue(options, "Label");            // MatchOnly
var dbValue2 = PropertyValueFactory.SingleChoice.MatchDbValue(options, "New", addOnMiss: true);
var bizValue = PropertyValueFactory.MultipleChoice.MatchBizValue(options, dbValues);
var tagIds = PropertyValueFactory.Tags.MatchDbValue(options, tagValues, addOnMiss: true);
var chains = PropertyValueFactory.Multilevel.MatchBizValue(options, dbValues);

// Build*/Build*Serialized exist for when you already hold ids/labels.
```

Non-reference types need no factory — serialize raw values with
`SerializeAsStandardValue` (see `standard-value.md`).

## Property Type Conversion

```csharp
// Using IPropertyTypeConverter (injected)
var result = await propertyTypeConverter.ConvertValueAsync(fromProperty, toProperty, dbValue);
var preview = await propertyTypeConverter.PreviewConversionAsync(fromProperty, toType, dbValues);
```

## MediaLibraryV2 Migration

**IMPORTANT**: MediaLibraryV2 is transitioning to MediaLibraryV2Multi.

```csharp
using Bakabase.Modules.Property.Components;

bool isLegacy = MediaLibraryV2Adapter.IsLegacyProperty(pool, id);
var multiDbValue = MediaLibraryV2Adapter.ToMultiDbValue(singleDbValue);
var normalized = MediaLibraryV2Adapter.ReadAsMulti(dbValue);
```

**For new code**: Use `ResourceProperty.MediaLibraryV2Multi` directly.

## Key Files

| Purpose | Path |
|---------|------|
| PropertySystem | `src/modules/Bakabase.Modules.Property/PropertySystem.cs` |
| PropertyValueFactory | `src/modules/Bakabase.Modules.Property/Components/PropertyValueFactory.cs` |
| PropertyValueMatchPolicy | `src/modules/Bakabase.Modules.Property/Abstractions/Components/PropertyValueMatchPolicy.cs` |
| BuiltinProperties | `src/modules/Bakabase.Modules.Property/Components/BuiltinProperty/BuiltinProperties.cs` |
| IPropertyTypeConverter | `src/modules/Bakabase.Modules.Property/Abstractions/Components/IPropertyTypeConverter.cs` |
| MediaLibraryV2Adapter | `src/modules/Bakabase.Modules.Property/Components/MediaLibraryV2Adapter.cs` |
| PropertyInternals (internal) | `src/modules/Bakabase.Modules.Property/Components/PropertyInternals.cs` |
| Property descriptors | `src/modules/Bakabase.Modules.Property/Components/Properties/` |

## Best Practices

1. **Descriptors are the source of truth** — a new PropertyType needs a
   descriptor; type attributes and search/index behavior all come from it.
2. **Choose the ToDbValue policy deliberately** — `AutoCreateOptions` on write
   paths (enhancers, user edits), `MatchOnly` for validation/lookup.
3. **Use PropertyValueFactory.{Type}.Match\*** only for reference types.
4. **Use MediaLibraryV2Multi** instead of MediaLibraryV2 for new code.
5. **Reference types store UUIDs** — never store display labels in DB.
6. **Frontend mirror**: `src/web/src/components/Property/PropertySystem.ts`
   keeps the same attribute table for the UI — update both when a mapping changes.
