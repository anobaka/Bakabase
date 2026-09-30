import { describe, expect, it } from "vitest";

import {
  removeSubmittedSelection,
  shouldStartResourceMove,
  snapshotMoveSelection,
} from "../resourceMoveSelection";

import { getSelectionMoveBlockReasons } from "@/components/Resource/resourceMoveEligibility";
import { ResourceSource } from "@/sdk/constants";

const noModifiers = { altKey: false, ctrlKey: false, metaKey: false, shiftKey: false };

describe("resource move selection", () => {
  it("moves only selected cards with the panel enabled and no selection modifiers", () => {
    expect(shouldStartResourceMove(true, true, noModifiers)).toBe(true);
    expect(shouldStartResourceMove(false, true, noModifiers)).toBe(false);
    expect(shouldStartResourceMove(true, false, noModifiers)).toBe(false);
    for (const modifier of Object.keys(noModifiers)) {
      expect(shouldStartResourceMove(true, true, { ...noModifiers, [modifier]: true })).toBe(false);
    }
  });

  it("freezes the source paths and preserves selections outside the loaded page", () => {
    const resources = [
      { id: 1, path: "/old/one" },
      { id: 2, path: "/old/two" },
    ];
    const snapshot = snapshotMoveSelection([2, 3, 2], resources);

    resources[1].path = "/new/two";
    expect(snapshot).toEqual([
      { id: 2, path: "/old/two" },
      { id: 3, path: undefined },
    ]);
  });

  it("submission only removes its own ids and never clears another tab's selection", () => {
    const selected = [1, 2, 9];

    expect(
      removeSubmittedSelection(selected, "tab-x", { sourceTabId: "tab-x", resourceIds: [1, 2] }),
    ).toEqual([9]);
    expect(
      removeSubmittedSelection(selected, "tab-y", { sourceTabId: "tab-x", resourceIds: [1, 2] }),
    ).toBe(selected);
    expect(removeSubmittedSelection(selected, "tab-y", { resourceIds: [1, 2] })).toBe(selected);
  });

  it("keeps Steam and missing-file members in mixed drag payloads for preview exclusions", () => {
    const resources = [
      {
        id: 1,
        path: "/steam/game",
        displayName: "Steam game",
        sourceLinks: [{ source: ResourceSource.Steam }],
      },
      { id: 2, path: "/local/movie", displayName: "Movie", hasLocalPath: true },
      { id: 3, hasLocalPath: false },
    ];
    const ids = [1, 2, 3, 99];
    const blocked = getSelectionMoveBlockReasons(ids, resources).length > 0;

    expect(shouldStartResourceMove(true, true, noModifiers, blocked)).toBe(true);
    expect(snapshotMoveSelection(ids, resources)).toEqual([
      { id: 1, path: "/steam/game", displayName: "Steam game" },
      { id: 2, path: "/local/movie", displayName: "Movie" },
      { id: 3, path: undefined },
      { id: 99, path: undefined },
    ]);
    expect(shouldStartResourceMove(true, false, noModifiers, blocked)).toBe(false);
    expect(shouldStartResourceMove(true, true, { ...noModifiers, ctrlKey: true }, blocked)).toBe(
      false,
    );
  });

  it("preserves rectangle selection when every selected resource is known immovable", () => {
    const resources = [
      { id: 1, sourceLinks: [{ source: ResourceSource.Steam }] },
      { id: 2, hasLocalPath: false },
    ];
    const blocked = getSelectionMoveBlockReasons([1, 2], resources).length > 0;

    expect(shouldStartResourceMove(true, true, noModifiers, blocked)).toBe(false);
  });
});
