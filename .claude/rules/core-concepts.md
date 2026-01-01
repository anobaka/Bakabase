---
paths:
  - "**/abstractions/**"
  - "**/modules/**"
  - "**/legacy/**"
  - "**/Bakabase.Service/**"
---

# Core Data Model

## Resource
A managed file or folder with metadata.

## Property
Resource attribute with 3 pools (via `PropertyPool`):
- `Internal`: system-managed, single value per resource, no scope
- `Reserved`: system-defined but user-editable (Rating, Introduction, CoverPaths, etc.)
- `Custom`: user-defined properties

## PropertyValueScope
Multi-dimensional values for Reserved/Custom properties. Same property can have different values in different scopes to avoid conflicts. Internal properties excluded.

## StandardValue
Primitive data types defining property storage and display. Each property type maps to specific StandardValue types.

## Path Mark
Declarative binding between filesystem paths and system entities:
- Mark types: Resource | Property | MediaLibrary link
- Scope: single path, hierarchy pattern, or regex
- Sync: mark change → auto/manual sync → creates/removes resources, properties, associations

# Enhancement System

## Enhancer
Plugins that populate resource properties or files (covers, subtitles, etc.)
- Config location: ResourceProfile (upcoming), currently MediaLibraryV2
- Per-enhancer settings, supports priority ordering and chaining

# Playback

## Player
File opener config (system default or custom external tool)

## PlayableFiles
Identifies playable files within a resource
- **PlayableFileLocator/Selector**: Rule-based identification of playable files

# Background Services

- **BTask**: Built-in background task manager
- **DownloadTask**: Built-in third-party resource downloaders
