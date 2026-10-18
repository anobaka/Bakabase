import { describe, expect, it } from "vitest";

import {
  mergeMediaLibraryOrder,
  orderMediaLibraryChoices,
  reorderVisibleMediaLibraries,
} from "../mediaLibraryOrder";
import { mediaLibraryChoiceValue } from "../mediaLibraryFilter";

describe("shared media-library display order", () => {
  it("preserves hidden/offline positions and only moves currently visible slots", () => {
    expect(
      reorderVisibleMediaLibraries(["A", "hidden", "B", "offline", "C"], ["A", "B", "C"], "C", "A"),
    ).toEqual(["C", "hidden", "A", "offline", "B"]);
    expect(reorderVisibleMediaLibraries(["A"], ["A"], "missing", "A")).toEqual(["A"]);
  });

  it("keeps disconnected preferences and appends newly created libraries once", () => {
    expect(mergeMediaLibraryOrder(["offline", "known"], ["known", "new", "new"])).toEqual([
      "offline",
      "known",
      "new",
    ]);
  });

  it("separates overlapping numeric IDs and different incarnations of the same device", () => {
    const local = mediaLibraryChoiceValue({ serverId: "local", libraryEpoch: "old" }, 1);
    const replacement = mediaLibraryChoiceValue({ serverId: "local", libraryEpoch: "new" }, 1);
    const remote = mediaLibraryChoiceValue({ serverId: "remote", libraryEpoch: "old" }, 1);
    const items = [{ key: local }, { key: replacement }, { key: remote }];

    expect(orderMediaLibraryChoices(items, [remote, local], ({ key }) => key)).toEqual([
      items[2],
      items[0],
      items[1],
    ]);
  });
});
