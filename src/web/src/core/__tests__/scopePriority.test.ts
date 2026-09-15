import type { PropertyValueScopePreference, Value } from "@/core/models/Resource";

import { describe, expect, it } from "vitest";

import { buildEffectiveScopePriority, resolveScopedValue } from "@/core/models/Resource";
import { PropertyPool, PropertyValueScope } from "@/sdk/constants";

const { Manual, Synchronization, Av } = PropertyValueScope;
const globalPriority = [Manual, Synchronization, Av];
const values = (...entries: [PropertyValueScope, unknown][]): Value[] =>
  entries.map(([scope, bizValue]) => ({ scope, bizValue }));
const preference = (...entries: [PropertyValueScope, boolean][]): PropertyValueScopePreference => ({
  resourceId: 1,
  propertyPool: PropertyPool.Custom,
  propertyId: 7,
  priorities: entries.map(([scope, fallbackOnEmpty]) => ({ scope, fallbackOnEmpty })),
});

describe("resource property scope priority", () => {
  it.each([undefined, null, []])(
    "preserves global behavior without profile priority (%s)",
    (profile) => {
      expect(buildEffectiveScopePriority(globalPriority, undefined, profile)).toEqual(
        globalPriority,
      );
      expect(
        resolveScopedValue(
          values([Manual, "Manual"], [Av, "AV"]),
          globalPriority,
          undefined,
          profile,
        )?.bizValue,
      ).toBe("Manual");
    },
  );

  it("uses profile priority before global and keeps every remaining scope once", () => {
    expect(buildEffectiveScopePriority(globalPriority, undefined, [Av, Av])).toEqual([
      Av,
      Manual,
      Synchronization,
    ]);
    expect(
      resolveScopedValue(values([Manual, "Manual"], [Av, "Profile"]), globalPriority, undefined, [
        Av,
      ])?.bizValue,
    ).toBe("Profile");
  });

  it("falls through an empty profile scope in the remaining global order", () => {
    expect(
      resolveScopedValue(
        values([Av, " "], [Manual, "Manual"], [Synchronization, "Synchronized"]),
        [Synchronization, Manual, Av],
        undefined,
        [Av],
      )?.bizValue,
    ).toBe("Synchronized");
  });

  it("lets a resource override replace profile priority and restores profile on reset", () => {
    const candidates = values([Manual, "Resource"], [Av, "Profile"]);

    expect(
      resolveScopedValue(candidates, globalPriority, preference([Manual, true]), [Av])?.bizValue,
    ).toBe("Resource");
    expect(resolveScopedValue(candidates, globalPriority, preference(), [Av])?.bizValue).toBe(
      "Profile",
    );
  });

  it("keeps a resource cutoff from falling through to profile or global", () => {
    expect(
      resolveScopedValue(
        values([Manual, ""], [Av, "Profile"]),
        globalPriority,
        preference([Manual, false], [Av, true]),
        [Av],
      ),
    ).toBeUndefined();
  });

  it("continues only within the resource override's own fallback chain", () => {
    const candidates = values([Manual, null], [Synchronization, "Fallback"], [Av, "Profile"]);

    expect(
      resolveScopedValue(
        candidates,
        globalPriority,
        preference([Manual, true], [Synchronization, true]),
        [Av],
      )?.bizValue,
    ).toBe("Fallback");
    expect(
      resolveScopedValue(candidates, globalPriority, preference([Manual, true]), [Av]),
    ).toBeUndefined();
  });

  it.each([0, false])("keeps the nonempty profile value %s", (value) => {
    expect(
      resolveScopedValue(values([Manual, "Fallback"], [Av, value]), globalPriority, undefined, [Av])
        ?.bizValue,
    ).toBe(value);
  });
});
