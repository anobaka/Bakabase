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
yarn run gen-sdk  # Regenerate API SDK (offline; no running backend needed)
```

## SDK Generation

`yarn gen-sdk` is fully offline (no backend needed). See the root `CLAUDE.md`
**SDK Generation** section for mechanism and the `api-conventions.md` rule
for when to run it.

`src/web/src/sdk/` is **output only** — never hand-edit `Api.ts`,
`BApi2.d.ts`, or `constants.ts`. Source for those lives in
`src/Bakabase.Cli/` (swagger + constants generators) and
`src/web/tools/gen-sdk.js` (the orchestrator). If the frontend needs a
server-side static value, add it there — see `api-conventions.md`.

## i18n (Internationalization)

- Use **token-based keys** for translations
- Key format: `namespace.component.element.type`
- Examples:
  - `enhancer.bangumi.prioritySubjectType.label`
  - `enhancer.bangumi.prioritySubjectType.description`
  - `BangumiSubjectType.Anime`
- **DO NOT** use full text as translation keys
- Translation files location: `Bakabase/src/web/src/locales/{en,cn}/`
