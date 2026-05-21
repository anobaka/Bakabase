# Adding an Internal or Reserved Property

Builtin properties (`PropertyPool.Internal` and `PropertyPool.Reserved`) are
declared across many enumerative sites. Adding one touches a dozen-plus places
and most are **not** compile-checked — use this checklist so nothing is missed.
(Custom properties are user-defined at runtime and are not covered here.)

## Internal vs Reserved

|                          | Internal                                    | Reserved                                          |
|--------------------------|---------------------------------------------|---------------------------------------------------|
| User-editable            | No                                          | Yes                                               |
| Value scope              | None (single value)                         | Multi-scope                                       |
| Storage                  | Derived from `Resource` / property-specific | `ReservedPropertyValue` table (one column each)   |
| Enhancer target          | No                                          | Yes                                               |
| In `resource.Properties` | No (only `Reserved` + `Custom`)             | Yes (`PropertyPool.Reserved`)                     |

Both are `ResourceProperty` enum members registered in
`PropertyInternals.BuiltinPropertyMap`.

## Checklist

Tags: **[Both]** / **[Internal]** / **[Reserved]**. *(optional)* = only when applicable.

### 1. Definitions

- [ ] **[Both]** `ResourceProperty` enum — add the member.
  `src/legacy/Bakabase.InsideWorld.Models/Constants/ResourceProperty.cs`
- [ ] **[Reserved]** `ReservedProperty` enum — add the member (`= ResourceProperty.X`).
  `src/abstractions/Bakabase.Abstractions/Models/Domain/Constants/ReservedProperty.cs`
- [ ] **[Both]** `PropertyInternals.BuiltinPropertyMap` — add `new Property(pool, (int)ResourceProperty.X, PropertyType.Y)`.
  `src/modules/Bakabase.Modules.Property/Components/PropertyInternals.cs`
- [ ] **[Both]** Same file — add to `InternalPropertyMap` *(internal)* or `ReservedPropertyMap` *(reserved)*.
- [ ] **[Both]** `BuiltinProperties` — add a typed accessor (`BuiltinProperties.Internal.X` / `.Reserved.X`).
  `src/modules/Bakabase.Modules.Property/Components/BuiltinProperty/BuiltinProperties.cs`
- [ ] **[Both]** Localization — `BuiltinPropertyName_{X}` in **both** `PropertyResource.resx` and `PropertyResource.zh-Hans.resx`.
  `src/modules/Bakabase.Modules.Property/Resources/`

### 2. Storage **[Reserved]**

- [ ] DB model — add a column to `ReservedPropertyValue`.
  `src/abstractions/Bakabase.Abstractions/Models/Db/ReservedPropertyValue.cs`
- [ ] Domain model — add the field to `ReservedPropertyValue`.
  `src/abstractions/Bakabase.Abstractions/Models/Domain/ReservedPropertyValue.cs`
- [ ] `ReservedPropertyValueExtensions` — map the field in **both** `ToDomainModel` and `ToDbModel`.
  `src/modules/Bakabase.Modules.Property/Extensions/ReservedPropertyValueExtensions.cs`
- [ ] EF migration — `dotnet ef migrations add {Name} ...` (see root `CLAUDE.md`; keep it pure-schema).
- [ ] **Compiler-enforced** — `ReservedProperties` — add a `ReservedPropertyDefinition` and a `Get` switch arm.
  `src/abstractions/Bakabase.Abstractions/Extensions/ReservedPropertyValueAccessor.cs`
  Omitting this fails the build (CS8509). Field read/write for every other
  consumer routes through this accessor automatically.

*(Internal properties have no values table — decide where the value comes from:
a `Resource` field, the filesystem cache, or property-specific storage.)*

### 3. Per-property logic **[Reserved]**

These use the accessor for the field itself, but the surrounding logic is per-site:

- [ ] `ResourceService` — the `resource.Properties[Reserved]` population block.
- [ ] `ResourceService.ApplyReservedPropertyValue` — the per-`PropertyType` deserialization switch.
- [ ] `ReservedPropertyValueService` — the change-detection switch (`default: throw`, so it fails loudly if missed).

### 4. Search *(optional — only if the property should be filterable)*

- [ ] **[Both]** `SearchableReservedProperty` enum — add the member (despite the name this enum also lists internal properties).
  `src/legacy/Bakabase.InsideWorld.Models/Constants/SearchableReservedProperty.cs`
- [ ] **[Both]** `ResourceSearchIndexService` — add the index strategy (value / range / multi-key — see `resource-search-index.md`).

### 5. Enhancer **[Reserved]** *(optional — only if enhancer-populated)*

- [ ] Add `reservedPropertyCandidate: ReservedProperty.X` to the relevant `*EnhancerTarget` descriptors.
  `src/modules/Bakabase.Modules.Enhancer/Components/Enhancers/*/`
- [ ] `EnhancerResource.resx` — add a target label if needed.
- *(`EnhancerService` writes the value via the accessor — automatic.)*

### 6. Metadata sync **[Reserved]** *(optional)*

- [ ] `ResourceSyncService` / `ResourceDataTask` — auto-populate from external source metadata, if applicable.
- *(`SourceMetadataSyncService` writes the value via the accessor — automatic.)*

### 7. Display-name templates **[Both]** *(optional — only if usable as a `{placeholder}`)*

- [ ] `BuiltinPropertyForDisplayName` enum.
  `src/legacy/Bakabase.InsideWorld.Business/Models/Domain/Constants/BuiltinPropertyForDisplayName.cs`
- [ ] `ResourceService.BuildDisplayNameSegmentsForResource` **and** `BuildDisplayNameForResourceOptimized` — resolve the value.
- [ ] `enum.builtinPropertyForDisplayName.{x}` in `src/web/src/locales/{en,cn}/enums.json` (must match the server resx name).

### 8. Frontend

- [ ] **[Both]** `yarn gen-sdk` — regenerates `constants.ts` / `Api.ts` / `BApi2.d.ts` from the C# enums/models. Required after any enum or model change.
- [ ] **[Reserved]** `Enhancement.ts` — add the field to `reservedPropertyValue`.
  `src/web/src/core/models/Enhancement.ts`
- [ ] **[Reserved]** `ResourceEnhancementsModal.tsx` — the reserved-property render switch.
- [ ] **[Both]** Resource detail UI *(optional)* — `DetailModal`, `ResourceDetailLayoutEditor`
  (`SamplePlaceholders` / `defaultLayout` / `types`), and `locales/{en,cn}/pages/resource.json`.
- [ ] **[Both]** `enums.json` — `enum.*` keys if the enum value is shown in the UI via `getEnumKey`.

## Build

- `dotnet build` after C# changes; `dotnet ef migrations add` for reserved-property columns.
- `yarn gen-sdk` after any enum/model change, then commit the regenerated SDK.

## Extending the compile-time net

Today only the accessor registration (step 2) is compile-checked — the
`ReservedProperties.Get` switch has no discard arm, so a missing
`ReservedProperty` fails the build (CS8509, promoted to error via the
root `.editorconfig`). To pull another concern into the same net, add it as a
constructor parameter on `ReservedPropertyDefinition`: every definition then
fails to compile (CS7036) until updated. Concerns that cannot be expressed in
C# (frontend, resx) still rely on this checklist.
