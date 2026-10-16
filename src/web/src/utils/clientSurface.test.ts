import { describe, expect, it } from "vitest";

import { isDesktopAppWindow } from "./clientSurface";

describe("desktop window presentation marker", () => {
  it("distinguishes the embedded app window from an ordinary browser regardless of host address", () => {
    expect(isDesktopAppWindow("Mozilla/5.0 Chrome/120.0 Safari/537.36")).toBe(false);
    expect(isDesktopAppWindow("Mozilla/5.0 BakabaseDesktop/1.0")).toBe(true);
    expect(isDesktopAppWindow("BakabaseDesktop/1.0 Mozilla/5.0")).toBe(true);
    expect(isDesktopAppWindow("Mozilla/5.0 NotBakabaseDesktop/1.0")).toBe(false);
  });
});
