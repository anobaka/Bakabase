import { describe, expect, it, vi } from "vitest";

import { groupDestinations, parseMovePayload, reorderDestinations, clampGeometry } from "../utils";
vi.mock("@/components/ResourceMovePanel/api", () => ({
  movePanelApi: {},
  MoveRequestRejected: class extends Error {},
}));
const d = (id: string, path: string, order = 0, scope: "global" | "tab" = "global") => ({
  id,
  path,
  order,
  scope,
});

describe("move destinations", () => {
  it("does not merge separated path groups and preserves hand ordering", () => {
    const targets = [d("1", "/media/a", 0), d("2", "/other/x", 1), d("3", "/media/b", 2)];

    expect(groupDestinations(targets, true).map((g) => g.destinations.map((v) => v.id))).toEqual([
      ["1"],
      ["2"],
      ["3"],
    ]);
    const sorted = reorderDestinations(targets, "3", "2").sort((a, b) => a.order - b.order);

    expect(groupDestinations(sorted, true)[0]).toMatchObject({
      prefix: "/media",
      destinations: [{ id: "1" }, { id: "3" }],
    });
  });
  it("refuses cross-scope sorting and malformed external drops", () => {
    const targets = [d("1", "/a"), d("2", "/b", 0, "tab")];

    expect(reorderDestinations(targets, "1", "2")).toBe(targets);
    expect(parseMovePayload('{"resources":[{"id":"/file"}]}')).toBeUndefined();
    expect(
      parseMovePayload(
        '{"sourceContext":{"nodeId":"local","libraryEpoch":"epoch"},"resources":[{"id":1,"path":"/a"}]}',
      )?.resources[0].id,
    ).toBe(1);
  });
  it("keeps the window reachable after the viewport shrinks", () => {
    const next = clampGeometry({ x: 1600, y: 800, width: 600, height: 800 }, 800, 600);

    expect(next.x + next.width).toBeLessThanOrEqual(800);
    expect(next.y + next.height).toBeLessThanOrEqual(600);
  });
  it("preserves the drag source identity and rejects bare or federated resource payloads", () => {
    const sourceContext = { nodeId: "node", libraryEpoch: "library" };

    expect(
      parseMovePayload(
        JSON.stringify({ sourceContext, resources: [{ id: 1 }], sourceTabId: "other-tab" }),
      ),
    ).toMatchObject({ sourceContext, sourceTabId: "other-tab", resources: [{ id: 1 }] });
    expect(parseMovePayload(JSON.stringify({ resources: [{ id: 1 }] }))).toBeUndefined();
    expect(
      parseMovePayload(
        JSON.stringify({
          sourceContext,
          resources: [
            { id: 1, ref: { nodeId: "remote", libraryEpoch: "remote-library", resourceId: 1 } },
          ],
        }),
      ),
    ).toBeUndefined();
  });
});
