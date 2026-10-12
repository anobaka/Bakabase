import { describe, expect, it } from "vitest";

import { initialWindowBounds } from "./bounds";
import { WindowManager } from "./WindowManager";
describe("initial window viewport bounds", () => {
  it("centers a large media player within a 720-pixel-high screen including its footer", () => {
    const bounds = initialWindowBounds({ initialSize: { width: 1120, height: 760 } }, 1280, 720);

    expect(bounds).toMatchObject({ x: 80, y: 16, width: 1120, height: 688 });
    expect(bounds.y + bounds.height).toBeLessThanOrEqual(720);
  });
  it("honors usable minimums and clamps offscreen positions while fitting narrow viewports", () => {
    expect(
      initialWindowBounds(
        { initialSize: { width: 10, height: 10 }, initialPosition: { x: -20, y: 999 } },
        1280,
        720,
      ),
    ).toMatchObject({ width: 400, height: 300, x: 0, y: 420 });
    const small = initialWindowBounds({ minWidth: 400, minHeight: 300 }, 320, 240);

    expect(small).toMatchObject({
      x: 16,
      y: 16,
      width: 288,
      height: 208,
      minWidth: 288,
      minHeight: 208,
    });
    expect(initialWindowBounds(undefined, 16, 16).x).toBeGreaterThanOrEqual(0);
  });
  it("keeps a newly stacked window inside the screen after applying its cascade offset", () => {
    const manager = WindowManager.getInstance();
    const bounds = { x: 16, y: 16, width: 1200, height: 688, innerWidth: 1280, innerHeight: 720 };
    const first = manager.registerWindow("viewport-test-1", bounds);
    const second = manager.registerWindow("viewport-test-2", bounds);

    expect(second.y + second.height).toBeLessThanOrEqual(720);
    expect(second.x + second.width).toBeLessThanOrEqual(1280);
    manager.unregisterWindow(first.id);
    manager.unregisterWindow(second.id);
  });
  it("keeps exactly one focused window topmost after a long session", () => {
    const manager = WindowManager.getInstance();
    const first = manager.registerWindow("long-session-1", {});
    const second = manager.registerWindow("long-session-2", {});

    for (let index = 0; index < 1100; index++) {
      manager.bringToFront(index % 2 === 0 ? first.id : second.id);
    }
    expect(manager.isTopmost(second.id)).toBe(true);
    expect(manager.isTopmost(first.id)).toBe(false);
    manager.bringToFront(first.id);
    expect(manager.isTopmost(first.id)).toBe(true);
    expect(manager.isTopmost(second.id)).toBe(false);
    manager.unregisterWindow(first.id);
    manager.unregisterWindow(second.id);
  });

  it("skips a reflected position already occupied and fits a viewport with no spare room", () => {
    const manager = WindowManager.getInstance();
    const bounds = { x: 780, y: 680, width: 800, height: 500, innerWidth: 1600, innerHeight: 1200 };
    const first = manager.registerWindow("reflected-1", bounds);
    const second = manager.registerWindow("reflected-2", bounds);

    expect(first).toMatchObject({ x: 780, y: 680 });
    expect(second).toMatchObject({ x: 740, y: 640 });
    manager.unregisterWindow(first.id);
    manager.unregisterWindow(second.id);
    const full = { x: 0, y: 0, width: 320, height: 240, innerWidth: 320, innerHeight: 240 };
    manager.registerWindow("full-1", full);
    expect(manager.registerWindow("full-2", full)).toMatchObject({ x: 0, y: 0 });
    manager.unregisterWindow("full-1");
    manager.unregisterWindow("full-2");
  });
});
