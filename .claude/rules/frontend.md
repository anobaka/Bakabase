---
paths:
  - "**/web/**"
  - "**/*.tsx"
  - "**/*.ts"
---

# Frontend Development

## Stack
- React with TypeScript
- Location: `Bakabase/src/web/`

## Commands

```bash
yarn dev          # Start dev server
yarn run gen-sdk  # Regenerate API SDK after backend changes
```

## SDK Generation

When backend API changes (endpoints, request/response types), regenerate the SDK:
```bash
cd Bakabase/src/web && yarn run gen-sdk
```

Commit the generated SDK files together with the API changes.
