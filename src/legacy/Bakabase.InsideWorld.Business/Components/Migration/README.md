# MigrationHelper – Responsibilities

## 1. Category & Media Library Migration
In `v2.0.0`, `Category` and `MediaLibrary` are deprecated. They are replaced by **MediaLibraryTemplate** and **MediaLibraryV2**. Migration steps:

### Step 1 – Load Data
- Load all `Categories` and `MediaLibraries` including their `additionalItems`.

### Step 2 – Create MediaLibraryTemplate & MediaLibraryV2 (per PathConfiguration)
For each `PathConfiguration` in a `MediaLibrary`:

1. **Create MediaLibraryTemplate**
   - Use the `PathConfiguration` to create a `MediaLibraryTemplate`.
   - Set `MediaLibraryTemplate.Name` as:
     ```
     {Category.Name}-{MediaLibrary.Name}-{PathConfiguration.Path}-{PathConfiguration.Index}
     ```
   - Populate:
     - `PlayableFileLocator` ← from `Category.ComponentsData.PlayableFileSelector`
     - `Enhancers` ← from `Category.EnhancerOptions`
     - `DisplayNameTemplate` ← from `Category.DisplayNameTemplate`

2. **Create MediaLibraryV2**
   - Name: same as above.
   - Link to the created `MediaLibraryTemplate`.
   - Configure:
     - `Players` ← from `Category.ComponentsData.Player`
     - `Paths` ← from `PathConfiguration.Path`
     - `Color` ← from `Category.Color`

### Step 3 – Update Resources
- For all resources linked to the old `Category` or `MediaLibrary`:
  - Set `categoryId = 0`
  - Set `mediaLibraryId = {id of the new MediaLibraryV2}`

---

## 1.1 Idempotency

- If a `MediaLibraryTemplate` with the same `Name` exists, reuse it (skip creation).
- If a `MediaLibraryV2` with the same `Name` exists, reuse it (skip creation).
- Always ensure `MediaLibraryV2.TemplateId` is linked to the template (patch when different).
- Resource reassignment uses the longest path-prefix match against each `PathConfiguration.Path`.