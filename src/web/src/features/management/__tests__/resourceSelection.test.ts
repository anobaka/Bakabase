import { describe, expect, it } from "vitest";

import { groupResourceSelection } from "../resourceSelection";
import { resourceSelectionKey } from "../resourceIdentity";
import type { ManagementSource } from "../api";
import type { Resource } from "@/core/models/Resource";

const owner = (serverId: string, isLocal = false): ManagementSource => ({
  serverId,
  isLocal,
  name: serverId,
  libraryEpoch: "epoch",
  available: true,
  contractVersion: 1,
});
const resource = (source: ManagementSource, id: number) => ({ id, owner: source }) as Resource;

describe("owner-qualified resource selections", () => {
  it("keeps identical IDs on three devices in independent action batches", () => {
    const sources = [owner("local", true), owner("one"), owner("two")];
    const rows = sources.map((source) => resource(source, 7));
    const keys = rows.map(resourceSelectionKey);

    expect(new Set(keys).size).toBe(3);
    expect(keys[0]).toBe(7);
    expect(groupResourceSelection(keys, rows, sources)).toEqual(
      sources.map((source, index) => ({
        owner: source,
        ids: [7],
        resources: [rows[index]],
      })),
    );
  });

  it("retains selected but unloaded remote IDs without borrowing local records", () => {
    const source = owner("remote");
    const rows = [resource(owner("local", true), 7)];

    expect(
      groupResourceSelection([resourceSelectionKey(resource(source, 7))], rows, [source]),
    ).toEqual([{ owner: source, ids: [7], resources: [] }]);
  });

  it("never resolves a selection from an old library generation as a local ID", () => {
    const old = owner("remote");
    const replacement = { ...old, libraryEpoch: "new-epoch" };

    expect(
      groupResourceSelection([resourceSelectionKey(resource(old, 7))], [], [replacement]),
    ).toEqual([]);
  });
});
