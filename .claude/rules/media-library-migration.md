---
paths:
  - "**/MediaLibrary*"
  - "**/Category*"
  - "**/ResourceProfile*"
  - "**/Template*"
---

# Media Library Migration Guide

## Overview

Media library configuration is migrating through 3 generations:
```
Category (v1) → MediaLibraryV2 (v2) → ResourceProfile (v3)
   ❌ deprecated    ⚠️ transitioning      🚧 upcoming
```

## Architecture Evolution

### Generation 1: Category (deprecated)
- Monolithic library config
- Status: **Dead code, do not use**
- Location: `src/legacy/` (if any remains)

### Generation 2: MediaLibraryV2 (current, partially deprecated)
- Bound to `MediaLibraryTemplate` for preset configurations
- Template contained: resource location, property mapping, playable file rules, enhancer config, parent-child relationships
- **What's changing**: Template binding being removed
- **What it becomes**: Simple multi-select property (like custom properties)
- Status: **Actively used, but avoid new Template dependencies**

#### MediaLibraryV2 → MediaLibraryV2Multi Migration

MediaLibraryV2 is transitioning from SingleChoice to MultipleChoice:

```
ResourceProperty.MediaLibraryV2      → SingleChoice (DEPRECATED, facade only)
ResourceProperty.MediaLibraryV2Multi → MultipleChoice (actual storage)
```

**Migration Pattern (Facade Strategy)**:
- `MediaLibraryV2` definition remains `SingleChoice` (type consistency)
- All V2 operations internally redirect to V2Multi
- Read: Multi → Single (return first value only)
- Write: Single → Multi (wrap in list)
- V2Multi operates normally without facade

**For new code**: Use `ResourceProperty.MediaLibraryV2Multi` directly.

**For existing code**: Continue using `MediaLibraryV2` until migration complete.

**Cleanup**: Remove V2 and facade when all external references migrated.

### Generation 3: ResourceProfile (upcoming)
- Will absorb configurations currently in MediaLibraryTemplate:
  - Enhancer configuration
  - Property mapping rules
  - Playable file rules
- Status: **Under development**

## Migration Rules for Claude

### When modifying existing code:
1. **Category**: Do not touch. If you find active usage, flag it.
2. **MediaLibraryTemplate**: No new features. Bug fixes only.
3. **MediaLibraryV2 + Template binding**: Avoid strengthening this coupling.

### When adding new features:
1. Check if feature belongs to ResourceProfile scope
2. If yes → implement in ResourceProfile (or stub for future)
3. If no → implement in MediaLibraryV2 without Template dependency

### Code locations:
| Concept | Current Location | Target Location |
|---------|------------------|-----------------|
| Enhancer config | MediaLibraryTemplate | ResourceProfile |
| Property mapping | MediaLibraryTemplate | ResourceProfile |
| Playable file rules | MediaLibraryTemplate | ResourceProfile |
| Library ↔ Resource binding | MediaLibraryV2 | MediaLibraryV2 (as property) |

## Related Files

- `src/modules/*/MediaLibrary*` - Current implementation
- `src/legacy/*Category*` - Legacy code (deprecated)
- `src/abstractions/*/ResourceProfile*` - New abstractions (if exists)

## Questions to Ask

If unclear whether to use v2 or v3 patterns, ask:
- "Is this feature tied to a library or to individual resources?"
- "Will this need enhancer/property/playable-file configuration?"