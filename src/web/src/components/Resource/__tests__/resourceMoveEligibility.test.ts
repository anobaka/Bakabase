import { describe, expect, it } from "vitest";

import { getKnownMoveBlockReason, getSelectionMoveBlockReasons } from "../resourceMoveEligibility";

import { ResourceSource } from "@/sdk/constants";

const steam = {
  id: 1,
  path: "/games/Steam/game",
  hasLocalPath: true,
  sourceLinks: [{ source: ResourceSource.Steam }],
};
const local = {
  id: 2,
  path: "/media/video",
  hasLocalPath: true,
  sourceLinks: [{ source: ResourceSource.PathMark }],
};

describe("resource move source eligibility", () => {
  it("blocks a Steam-owned installation, including when it also has a path mark", () => {
    expect(getKnownMoveBlockReason(steam)).toBe("steamManaged");
    expect(
      getKnownMoveBlockReason({
        ...steam,
        sourceLinks: [...steam.sourceLinks, ...local.sourceLinks],
      }),
    ).toBe("steamManaged");
  });

  it("does not confuse Steam metadata with ownership or guess about child installations", () => {
    const metadataOnly = {
      ...local,
      externalIdentities: [{ source: ResourceSource.Steam }],
      path: "/games/Steam",
    };

    expect(getKnownMoveBlockReason(metadataOnly)).toBeUndefined();
    expect(
      getKnownMoveBlockReason({ ...local, sourceLinks: [{ source: ResourceSource.DLsite }] }),
    ).toBeUndefined();
  });

  it.each([{ hasLocalPath: false }, { path: null }, { path: "" }])(
    "identifies explicit lack of local files: %o",
    (fields) => {
      expect(getKnownMoveBlockReason({ id: 3, ...fields })).toBe("noLocalFiles");
    },
  );

  it("leaves incomplete progressive-search and unloaded resources for authoritative preview", () => {
    expect(getKnownMoveBlockReason({ id: 3 })).toBeUndefined();
    expect(getSelectionMoveBlockReasons([1, 99], [steam])).toEqual([]);
    expect(getSelectionMoveBlockReasons([1, 3], [steam, { id: 3 }])).toEqual([]);
  });

  it("disables only an entirely known blocked selection and describes each reason", () => {
    expect(
      getSelectionMoveBlockReasons([1, 3, 1], [steam, { id: 3, hasLocalPath: false }]),
    ).toEqual(["steamManaged", "noLocalFiles"]);
    expect(getSelectionMoveBlockReasons([1, 2], [steam, local])).toEqual([]);
    expect(getSelectionMoveBlockReasons([], [steam, local])).toEqual([]);
  });
});
