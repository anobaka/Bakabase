---
paths:
  - "**/Bakabase.Service/**"
  - "**/*Controller.cs"
  - "**/*InputModel.cs"
  - "**/*ViewModel.cs"
  - "**/*ResponseModel.cs"
  - "**/Constants/**"
---

# API Conventions

See the root `CLAUDE.md` **SDK Generation** section for how `yarn gen-sdk`
works under the hood. The rules below cover **when** you need it.

## Regenerate the SDK after any of these

- Add/rename a controller action, or change its route/signature/response
- Add/modify a request/response model (`*InputModel` / `*ViewModel` /
  `*ResponseModel` / DTO)
- Change a `[SwaggerOperation]` attribute, swagger filter, or DI binding
  that shapes the OpenAPI schema
- Add/rename a `public enum` consumed by the frontend
- Change any static server-side data exposed via `constants.ts`
  (see "Static data for the frontend" below)

Then commit the regenerated `Api.ts` / `BApi2.d.ts` / `constants.ts`
together with the C# change.

## Static data for the frontend

When the frontend needs a server-side value that is **known at build time**
(`static readonly` dicts, sets, lists — `InternalOptions.MediaTypeExtensions`
is the canonical example), do **not** add a runtime HTTP endpoint.

Extend
[`BakabaseConstantsGenerator`](../../src/Bakabase.Service/Components/BakabaseConstantsGenerator.cs)
so the value ships through `src/web/src/sdk/constants.ts`. The frontend then
imports it directly, no fetch, no loading state.

Use a runtime endpoint only when the value actually depends on runtime state
(DB, options, filesystem, user session, etc.).
