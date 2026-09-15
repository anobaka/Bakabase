---
paths:
  - "**/abstractions/**"
  - "**/modules/**"
  - "**/legacy/**"
  - "**/Bakabase.Service/**"
---

# Core Data Model

## Resource
A managed work with metadata and an optional local file or folder. A resource can exist before its files have been obtained.

## ResourceSource and External Identity
- `ResourceSource` describes content origins (PathMark, Steam, DLsite, ExHentai, Aigc, Pixiv), not metadata sites or a guarantee of automatic downloading.
- `ThirdPartyId` identifies a site. `ResourceExternalIdentity` associates a resource with a site's work via `(ThirdPartyId, ExternalId)`; Bangumi and VNDB belong here.
- Platform identities already carried by `ResourceSourceLink.SourceKey` are not duplicated into the external identity table.
- Subscription and placeholder identity inputs use `ThirdPartyId`; metadata associations must survive local file binding and resource merging without becoming content sources.
- Acquisition leads separately describe where to obtain the files.

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
