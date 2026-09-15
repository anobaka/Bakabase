import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { describe, expect, it } from "vitest";

import { workflowTriggerRegistry } from "../Triggers";
import { workflowIntegrationSurfaces } from "../integrationSurfaces";

describe("workflow source integration coverage", () => {
  it("keeps every first-party trigger discoverable from a source page", () => {
    const covered = new Set<string>(
      Object.values(workflowIntegrationSurfaces).flatMap((x) => [...x.triggerKinds]),
    );
    for (const trigger of Object.values(workflowTriggerRegistry)) {
      expect(covered.has(trigger.kind), `${trigger.kind} needs a source integration entry`).toBe(
        true,
      );
    }
    const supported = new Set(Object.values(workflowTriggerRegistry).map((x) => x.kind));
    for (const kind of covered)
      expect(supported.has(kind), `${kind} needs a trigger definition`).toBe(true);
  });

  it("mounts each declared entry in its source component", () => {
    for (const [surface, definition] of Object.entries(workflowIntegrationSurfaces)) {
      for (const component of definition.components) {
        const source = readFileSync(resolve(__dirname, "../../..", component), "utf8");
        expect(source).toMatch(new RegExp(`<WorkflowIntegrationHint\\s+surface="${surface}"`));
      }
    }
  });
});
