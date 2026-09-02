# StandardValue Module Guide

## Overview

`Bakabase.Modules.StandardValue` defines 9 standard data formats with serialization, conversion, and validation capabilities.

## StandardValue Types

| Type | CLR Type | Serialization Format |
|------|----------|---------------------|
| String | `string` | Raw string |
| ListString | `List<string>` | Comma-separated with escape |
| Decimal | `decimal` | Invariant numeric string |
| Link | `LinkValue` | `text,url` escaped |
| Boolean | `bool` | `True`/`False` (reads also accept lowercase and `1`/`0`) |
| DateTime | `DateTime` | Unix timestamp (ms) |
| Time | `TimeSpan` | Milliseconds |
| ListListString | `List<List<string>>` | `;` outer, `,` inner |
| ListTag | `List<TagValue>` | `group,name;...` |

There is **no wrapper type and no factory** — values are the raw CLR types above.
`TagValue` and `LinkValue` are immutable records (init-only).

## Serialization (the single API)

Serialization has exactly one API pair — the extension methods in
`Bakabase.Modules.StandardValue.Extensions`:

```csharp
using Bakabase.Modules.StandardValue.Extensions;

// Serialize (type-strict: a value of the wrong CLR type yields null)
string? serialized = myValue.SerializeAsStandardValue(StandardValueType.String);

// Deserialize (tolerant by default: malformed data yields null)
object? deserialized = serialized.DeserializeAsStandardValue(StandardValueType.String);

// Type-safe deserialize
string? typed = serialized.DeserializeAsStandardValue<string>(StandardValueType.String);

// Surface malformed data instead of swallowing it
var strict = serialized.DeserializeAsStandardValue(StandardValueType.Decimal, throwOnError: true);
```

Deserialization is deliberately tolerant by default because it reads legacy
stored data; pass `throwOnError: true` at call sites that should fail loudly.

## Conversion

```csharp
// Sync conversion (no custom datetime parsing)
object? converted = StandardValueSystem.Convert(value, fromType, toType);

// Async conversion with custom datetime parsing (use IStandardValueService)
var converted = await standardValueService.Convert(value, fromType, toType);

// Conversion rules ([Flags] describing what happens/is lost); the matrix in
// StandardValueInternals is the single source of truth and is tied to actual
// handler behavior by ConversionRuleMatrixConsistency tests.
var rules = StandardValueSystem.GetConversionRules(fromType, toType);
```

## Validation

```csharp
bool isValid = StandardValueSystem.Validate(value, StandardValueType.String);
StandardValueType? type = StandardValueSystem.InferType(myValue);
StandardValueType? type = StandardValueSystem.InferType<List<string>>();
```

## Enhancer value channel

Enhancers construct values through `IStandardValueBuilder` records
(`StringValueBuilder`, `ListTagValueBuilder`, …) declared in
`Bakabase.Modules.Property.Components.ValueBuilders` — that is their typed
channel into `EnhancementTargetValue`, not a general-purpose factory.

## Key Files

| Purpose | Path |
|---------|------|
| Type enum | `src/abstractions/.../StandardValueType.cs` |
| Entry Point | `src/modules/Bakabase.Modules.StandardValue/StandardValueSystem.cs` |
| Serialization extensions | `src/modules/Bakabase.Modules.StandardValue/Extensions/StandardValueExtensions.cs` |
| Handlers | `src/modules/Bakabase.Modules.StandardValue/Components/ValueHandlers/` |
| Conversion matrix | `src/modules/Bakabase.Modules.StandardValue/Abstractions/Configurations/StandardValueInternals.cs` |

## Best Practices

1. **One API per concept** — serialization via the extension methods,
   conversion/validation via `StandardValueSystem`. Do not add wrappers.
2. **Check type before serialization** — a mismatched CLR type serializes to null.
3. **Deserialization is tolerant by default** — opt into `throwOnError` where
   silent nulls would hide corruption.
4. **Frontend mirrors these conventions** in
   `src/web/src/components/StandardValue/helpers.ts` (same escape rules, same
   tolerant boolean forms) — keep them in sync when changing formats.
