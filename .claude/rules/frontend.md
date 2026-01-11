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

## i18n (Internationalization)

- Use **token-based keys** for translations
- Key format: `namespace.component.element.type`
- Examples:
  - `enhancer.bangumi.prioritySubjectType.label`
  - `enhancer.bangumi.prioritySubjectType.description`
  - `BangumiSubjectType.Anime`
- **DO NOT** use full text as translation keys
- Translation files location: `Bakabase/src/web/src/locales/{en,cn}/`
