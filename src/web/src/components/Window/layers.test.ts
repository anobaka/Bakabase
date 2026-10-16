import { describe, expect, it } from "vitest";

import { WindowManager } from "./WindowManager";
import { nestedWindowModalClassNames, WINDOW_Z_INDEX_MAX, windowModalClassNames } from "./layers";

const zIndex = (className: string) => Number(/z-\[(\d+)\]/.exec(className)?.[1]);

describe("dialogs opened from floating windows", () => {
  it("keeps both modal portal layers above windows, including after focus compaction", () => {
    const manager = new WindowManager();

    manager.registerWindow("player", {});
    for (let i = 0; i < WINDOW_Z_INDEX_MAX + 1; i++) manager.bringToFront("player");
    expect(manager.getWindow("player")!.zIndex).toBeLessThanOrEqual(WINDOW_Z_INDEX_MAX);
    expect(zIndex(windowModalClassNames.backdrop)).toBeGreaterThan(WINDOW_Z_INDEX_MAX);
    expect(zIndex(windowModalClassNames.wrapper)).toBeGreaterThan(
      zIndex(windowModalClassNames.backdrop),
    );
    expect(zIndex(nestedWindowModalClassNames.backdrop)).toBeGreaterThan(
      zIndex(windowModalClassNames.wrapper),
    );
    expect(zIndex(nestedWindowModalClassNames.wrapper)).toBeGreaterThan(
      zIndex(nestedWindowModalClassNames.backdrop),
    );
  });
});
