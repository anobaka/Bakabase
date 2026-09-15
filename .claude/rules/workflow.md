---
paths:
  - "**/*Workflow*/**"
  - "**/web/src/pages/**"
---

# Workflow triggers and source integration

When adding or changing a first-party workflow trigger:

- Set an explicit `ActivationMode` and `SourceModule` on `IWorkflowTrigger`. Manual replay support is a separate capability; never infer the activation mode from `SupportsManualRun`.
- Describe exactly when input is produced, what does not trigger it, how to configure it, and which payload and filters it accepts. Supply localized name and description keys in Chinese and English. The help directory reads the runtime trigger registry; do not maintain a second event catalogue.
- Add the frontend trigger presentation/guide and its source route. Mount `WorkflowIntegrationHint` near the relevant source action and register that surface in `components/Workflow/integrationSurfaces.ts`. Extend the coverage tests when an intentional backend-only trigger has no UI entry, documenting why.
- Test event payloads and matching semantics, including exclusions such as initial subscription sync and already-present collection members. A nonmatching event must remain silent; filter or preflight failures must be visible as failed attempts without executing activities.
- Include persisted and draft configuration validation as appropriate. Validation must be read-only and automatic, must reject stale results, and must not claim that static checks guarantee successful execution with future input or network conditions.
- Regenerate the SDK when trigger DTOs or enums change. Run metadata, source integration, validation and affected event regression tests before committing.
