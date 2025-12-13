---
paths:
  - "**/Bakabase.Service/**"
  - "**/Controllers/**"
  - "**/*Controller.cs"
  - "**/*Dto.cs"
---

# API Conventions

## When C# HTTP API signatures change

When endpoints, request types, or response types are modified:

1. Run `yarn run gen-sdk` in `src/web/`
2. Commit generated SDK files together with API changes

This ensures the frontend SDK stays in sync with backend changes.
