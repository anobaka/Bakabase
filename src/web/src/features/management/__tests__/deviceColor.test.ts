import { describe, expect, it } from "vitest";

import { deviceColor } from "../resourceIdentity";

describe("resolved device colors", () => {
  it("preserves the automatic palette in a form native color inputs accept", () => {
    // Existing hash hues: 198°, 19° and 175°, all at 70% saturation / 58% lightness.
    expect(deviceColor("owner-a")).toBe("#49b2df");
    expect(deviceColor("owner-b")).toBe("#df7849");
    expect(deviceColor("server-one")).toBe("#49dfd2");
    for (let index = 0; index < 360; index++) {
      expect(deviceColor(`device-${index}`)).toMatch(/^#[0-9a-f]{6}$/);
    }
  });

  it("preserves explicit colors and resolves invalid preferences exactly like automatic colors", () => {
    expect(deviceColor("owner-a", { "owner-a": "#AB12CD" })).toBe("#AB12CD");
    for (const invalid of ["", "blue", "#bad", "hsl(270 70% 58%)", "url(invalid)"]) {
      expect(deviceColor("owner-a", { "owner-a": invalid })).toBe(deviceColor("owner-a"));
    }
  });
});
