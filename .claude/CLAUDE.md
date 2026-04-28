# Bakabase

Local media manager for organizing files of any type.

## Project Structure

- **Bakabase/** - Main application
  - `src/web/` - React frontend (TypeScript)
  - `src/apps/Bakabase/` - C# Windows desktop entry point
  - `src/apps/Bakabase.Service/` - C# HTTP API layer
  - `src/apps/Bakabase.Cli/` - C# offline build-time tool (SDK/constants generation). Reserve for dev-time codegen only.
  - `src/abstractions/` - C# interfaces & shared types
  - `src/modules/` - C# feature modules (preferred location for new features)
  - `src/legacy/` - C# historical core code, actively maintained
    - New features: prefer `modules/`, but `legacy/` OK if scope is small or tightly coupled with existing legacy code
- **Bakabase.Infrastructures/** - C# hosting & DI setup
- **Bakabase.Updater/** - C# application updater
- **LazyMortal/** - C# internal base framework

## Module Dependencies

```
Bakabase (desktop) → Bakabase.Service → modules → abstractions
                                      ↘ abstractions ↗
```

## Common Commands

```bash
# Frontend
cd Bakabase/src/web && yarn dev          # Start dev server
cd Bakabase/src/web && yarn run gen-sdk  # Regenerate API SDK (offline, no running backend required)

# Backend
dotnet build                                    # Build solution
dotnet run --project Bakabase/src/apps/Bakabase  # Run desktop app

# Database Migration (NEVER create migration files manually, always use this command)
dotnet ef migrations add {MigrationName} --verbose --project src/legacy/Bakabase.InsideWorld.Business --startup-project src/apps/Bakabase.Service --context BakabaseDbContext
```

## SDK Generation

`yarn gen-sdk` runs fully offline — it drives `Bakabase.Cli` to emit a fresh
`swagger.json` + `constants.ts` from the built assemblies (no HTTP server,
no SQLite, no side effects), then feeds them through `swagger-typescript-api`
and `openapi-typescript`.

Pipeline source: `src/web/tools/gen-sdk.js` (orchestrator) +
`src/apps/Bakabase.Cli/` (swagger + constants emitters).

Outputs (all committed, **never hand-edit**):
- `src/web/src/sdk/Api.ts`
- `src/web/src/sdk/BApi2.d.ts`
- `src/web/src/sdk/constants.ts`

Scratch: `src/web/.sdk-cache/` (gitignored).

For **when** to regenerate and the rule about static data (e.g.
`ExtensionMediaTypes`) shipping via `constants.ts` instead of a runtime
endpoint, see `.claude/rules/api-conventions.md`.
