# Bakabase

Local media manager for organizing files of any type.

## Project Structure

- **Bakabase/** - Main application
  - `src/web/` - React frontend (TypeScript)
  - `src/Bakabase/` - C# Windows desktop entry point
  - `src/Bakabase.Service/` - C# HTTP API layer
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
cd Bakabase/src/web && yarn run gen-sdk  # Regenerate API SDK

# Backend
dotnet build                              # Build solution
dotnet run --project Bakabase/src/Bakabase  # Run desktop app

# Database Migration (NEVER create migration files manually, always use this command)
dotnet ef migrations add {MigrationName} --verbose --project src/legacy/Bakabase.InsideWorld.Business --startup-project src/Bakabase.Service --context BakabaseDbContext
```
